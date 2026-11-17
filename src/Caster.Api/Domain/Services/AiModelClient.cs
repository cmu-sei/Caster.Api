// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Logging;

namespace Caster.Api.Domain.Services
{
    public interface IAiModelClient
    {
        /// <summary>
        /// Sends one prompt and expects the model to answer by calling a single tool, so the answer
        /// is JSON that matches inputSchema. Returns the tool input.
        /// </summary>
        Task<JsonElement> InvokeToolAsync(string system, string prompt, string toolName, string toolDescription, JsonObject inputSchema, CancellationToken ct);
    }

    public class BedrockAiModelClient : IAiModelClient
    {
        // Every prompt starts with this, so the model knows the security content it is asked for is training material.
        public const string Purpose =
@"You work inside the Crucible cyber range, which the Software Engineering Institute at Carnegie Mellon University uses to train
cyber defenders, incident responders and government and military staff. The people asking build exercises: the training scenarios
and the networks they run on. Exercises are about attacks, vulnerabilities, threat actors and their effects by design: that is
what participants practice responding to. Vulnerabilities, organizations and threat actors in a scenario are fictional unless the planner names a real one.
Describe adversary activity as defenders, reporters and officials would see and report it. Never write working exploit code,
malware, or step-by-step attack instructions; the scenario does not need them.";

        private readonly AiOptions _options;
        private readonly ILogger<BedrockAiModelClient> _logger;

        public BedrockAiModelClient(AiOptions options, ILogger<BedrockAiModelClient> logger)
        {
            _options = options;
            _logger = logger;
        }

        public async Task<JsonElement> InvokeToolAsync(string system, string prompt, string toolName, string toolDescription, JsonObject inputSchema, CancellationToken ct)
        {
            if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ModelId))
                throw new InvalidOperationException("AI drafting is not configured on this server.");

            // the Anthropic Messages body; only max_tokens is sent so newer models that reject sampling settings work
            var body = new JsonObject
            {
                ["anthropic_version"] = "bedrock-2023-05-31",
                ["max_tokens"] = _options.MaxTokens,
                ["system"] = Purpose + "\n\n" + system,
                ["messages"] = new JsonArray(new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = prompt
                }),
                ["tools"] = new JsonArray(new JsonObject
                {
                    ["name"] = toolName,
                    ["description"] = toolDescription,
                    ["input_schema"] = inputSchema
                }),
                // newer models reject a forced tool choice, so the system prompt asks for the tool instead
                ["tool_choice"] = new JsonObject { ["type"] = "auto" }
            };

            var config = new AmazonBedrockRuntimeConfig
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(_options.Region),
                Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds)
            };
            using var client = new AmazonBedrockRuntimeClient(config);

            var response = await InvokeAsync(client, _options.ModelId, body, toolName, ct);
            // a safety classifier can decline training content; one retry on another model, as the Claude API's own fallback does
            if (StopReason(response) == "refusal" && !string.IsNullOrWhiteSpace(_options.FallbackModelId))
                response = await InvokeAsync(client, _options.FallbackModelId, body, toolName, ct);

            return RepairStringifiedJson(ReadToolInput(response), inputSchema);
        }

        /// <summary>
        /// Models sometimes send a large array or object of a tool input as a string holding its JSON.
        /// Parses such strings back wherever the schema expects an array or object.
        /// </summary>
        public static JsonElement RepairStringifiedJson(JsonElement input, JsonObject schema)
        {
            var node = JsonNode.Parse(input.GetRawText());
            return JsonSerializer.SerializeToElement(Repair(node, schema));
        }

        private static JsonNode Repair(JsonNode node, JsonNode schema)
        {
            var type = schema?["type"]?.GetValue<string>();
            if (node is JsonValue value && value.TryGetValue<string>(out var text) && type is "array" or "object")
            {
                try
                {
                    var parsed = JsonNode.Parse(text);
                    if ((type == "array" && parsed is JsonArray) || (type == "object" && parsed is JsonObject))
                        node = parsed;
                }
                catch (JsonException)
                {
                }
            }
            if (node is JsonArray array && schema?["items"] is JsonNode items)
            {
                for (var i = 0; i < array.Count; i++)
                    array[i] = Repair(array[i]?.DeepClone(), items);
            }
            else if (node is JsonObject obj && schema?["properties"] is JsonObject properties)
            {
                foreach (var name in obj.Select(p => p.Key).ToList())
                {
                    if (properties[name] is JsonNode propertySchema)
                        obj[name] = Repair(obj[name]?.DeepClone(), propertySchema);
                }
            }
            return node;
        }

        // Streams the answer, so a long one is not cut off by the request timeout, and returns it as one message.
        private async Task<JsonElement> InvokeAsync(AmazonBedrockRuntimeClient client, string modelId, JsonObject body, string toolName, CancellationToken ct)
        {
            var request = new InvokeModelWithResponseStreamRequest
            {
                ModelId = modelId,
                ContentType = "application/json",
                Accept = "application/json",
                Body = new MemoryStream(Encoding.UTF8.GetBytes(body.ToJsonString()))
            };
            using var response = await client.InvokeModelWithResponseStreamAsync(request, ct);
            var message = new StreamedMessage();
            await foreach (var item in response.Body.WithCancellation(ct))
            {
                if (item is PayloadPart part)
                {
                    using var reader = new StreamReader(part.Bytes);
                    message.Add(await reader.ReadToEndAsync(ct));
                }
            }

            var result = message.ToMessage();
            _logger.LogInformation("AI draft {Tool} on {Model} stopped with {StopReason} {StopDetails}: {Usage}",
                toolName, modelId, StopReason(result),
                result.TryGetProperty("stop_details", out var details) ? details.GetRawText() : null,
                result.TryGetProperty("usage", out var usage) ? usage.GetRawText() : null);
            return result;
        }

        private static string StopReason(JsonElement message) =>
            message.TryGetProperty("stop_reason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString() : null;

        /// <summary>
        /// Returns the input of the response's tool_use block, or explains why there is none.
        /// </summary>
        public static JsonElement ReadToolInput(JsonElement response)
        {
            var stopReason = StopReason(response);
            if (stopReason == "max_tokens")
                throw new InvalidOperationException("The model ran out of output tokens before finishing the draft. Ask for a smaller draft.");
            // a safety classifier declined the request: HTTP 200 with no tool_use, and a category in stop_details
            if (stopReason == "refusal")
            {
                var category = response.TryGetProperty("stop_details", out var details)
                    && details.ValueKind == JsonValueKind.Object
                    && details.TryGetProperty("category", out var c)
                    && c.ValueKind == JsonValueKind.String
                        ? c.GetString()
                        : null;
                throw new InvalidOperationException(category is null
                    ? "The model declined to write this draft. Reword the request."
                    : $"The model declined to write this draft (safety category: {category}). Reword the request.");
            }

            var toolUse = response.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
                ? content.EnumerateArray().FirstOrDefault(c => c.TryGetProperty("type", out var type) && type.GetString() == "tool_use")
                : default;
            if (toolUse.ValueKind == JsonValueKind.Undefined)
                throw new InvalidOperationException("The model answered without returning a draft. Try again or reword the request.");

            return toolUse.GetProperty("input").Clone();
        }
    }

    /// <summary>
    /// Puts the events of a streamed Messages response back together into the message a non-streamed call returns:
    /// stop_reason, stop_details, usage, and the content blocks with each tool_use input parsed.
    /// </summary>
    public class StreamedMessage
    {
        private readonly JsonObject _message = new() { ["content"] = new JsonArray() };
        private readonly SortedDictionary<int, JsonObject> _blocks = new();
        private readonly Dictionary<int, StringBuilder> _partialJson = new();

        public void Add(string eventJson)
        {
            var e = JsonNode.Parse(eventJson)?.AsObject();
            switch (e?["type"]?.GetValue<string>())
            {
                case "message_start":
                    if (e["message"]?["usage"] is JsonObject startUsage)
                        MergeUsage(startUsage);
                    break;
                case "content_block_start":
                    var index = e["index"].GetValue<int>();
                    _blocks[index] = e["content_block"].DeepClone().AsObject();
                    break;
                case "content_block_delta":
                    AddDelta(e["index"].GetValue<int>(), e["delta"].AsObject());
                    break;
                case "message_delta":
                    if (e["delta"] is JsonObject delta)
                    {
                        if (delta["stop_reason"] is JsonNode stopReason)
                            _message["stop_reason"] = stopReason.DeepClone();
                        if (delta["stop_details"] is JsonNode stopDetails)
                            _message["stop_details"] = stopDetails.DeepClone();
                    }
                    if (e["usage"] is JsonObject deltaUsage)
                        MergeUsage(deltaUsage);
                    break;
                case "error":
                    throw new InvalidOperationException($"The model stopped with an error: {e["error"]?["message"]?.GetValue<string>() ?? eventJson}");
            }
        }

        public JsonElement ToMessage()
        {
            var content = _message["content"].AsArray();
            content.Clear();
            foreach (var (index, block) in _blocks)
            {
                if (_partialJson.TryGetValue(index, out var json))
                    block["input"] = json.Length == 0 ? new JsonObject() : JsonNode.Parse(json.ToString());
                content.Add(block.DeepClone());
            }
            return JsonSerializer.SerializeToElement(_message);
        }

        private void AddDelta(int index, JsonObject delta)
        {
            if (!_blocks.TryGetValue(index, out var block))
                return;
            switch (delta["type"]?.GetValue<string>())
            {
                case "input_json_delta":
                    if (!_partialJson.TryGetValue(index, out var json))
                        _partialJson[index] = json = new StringBuilder();
                    json.Append(delta["partial_json"]?.GetValue<string>());
                    break;
                case "text_delta":
                    block["text"] = (block["text"]?.GetValue<string>() ?? "") + delta["text"]?.GetValue<string>();
                    break;
            }
        }

        // message_start carries the input counts, message_delta the running output counts; later values win
        private void MergeUsage(JsonObject usage)
        {
            if (_message["usage"] is not JsonObject merged)
                _message["usage"] = merged = new JsonObject();
            foreach (var (key, value) in usage)
                merged[key] = value?.DeepClone();
        }
    }
}
