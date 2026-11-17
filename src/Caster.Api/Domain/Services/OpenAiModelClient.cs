// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Logging;

namespace Caster.Api.Domain.Services
{
    /// <summary>
    /// Asks a model behind an OpenAI-style chat completions endpoint to answer by calling a tool: the OpenAI API, or a server that
    /// offers the same API for local models, such as vLLM or Ollama. The answer is streamed, so a long one is not cut off by the
    /// request timeout.
    /// </summary>
    public class OpenAiModelClient : IAiProviderClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly AiOptions _options;
        private readonly IAiUsageRecorder _usage;
        private readonly ILogger<OpenAiModelClient> _logger;

        public OpenAiModelClient(IHttpClientFactory httpClientFactory, AiOptions options, IAiUsageRecorder usage, ILogger<OpenAiModelClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options;
            _usage = usage;
            _logger = logger;
        }

        public async Task<JsonElement> InvokeToolAsync(AiTaskModel model, string system, string prompt, string toolName, string toolDescription, JsonObject inputSchema, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(model.BaseUrl))
                throw new InvalidOperationException("The OpenAI provider needs a BaseUrl, e.g. https://api.openai.com/v1.");

            var body = new JsonObject
            {
                ["model"] = model.ModelId,
                ["max_completion_tokens"] = model.MaxTokens,
                ["stream"] = true,
                // the last event of the stream carries the token counts
                ["stream_options"] = new JsonObject { ["include_usage"] = true },
                ["messages"] = new JsonArray(
                    new JsonObject { ["role"] = "system", ["content"] = BedrockAiModelClient.Purpose + "\n\n" + system },
                    new JsonObject { ["role"] = "user", ["content"] = prompt }),
                ["tools"] = new JsonArray(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = toolName,
                        ["description"] = toolDescription,
                        ["parameters"] = inputSchema.DeepClone()
                    }
                }),
                // naming the tool makes smaller models call it rather than answer in prose
                ["tool_choice"] = new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = toolName } }
            };

            // OpenAI's reasoning models and gpt-oss take low, medium or high
            if (!string.IsNullOrWhiteSpace(model.Effort))
                body["reasoning_effort"] = model.Effort.Trim().ToLowerInvariant() switch { "low" => "low", "medium" => "medium", _ => "high" };

            StreamedCompletion answer;
            try
            {
                answer = await StreamAsync(model, body, ct);
            }
            catch (InvalidOperationException ex) when (body.ContainsKey("reasoning_effort") && ex.Message.Contains("reasoning_effort", StringComparison.OrdinalIgnoreCase))
            {
                // a model that does not reason refuses the setting
                body.Remove("reasoning_effort");
                answer = await StreamAsync(model, body, ct);
            }
            // logged in the Messages API's terms, as the Bedrock calls are
            var usage = answer.ToUsageMessage();
            _logger.LogInformation("AI draft {Tool} on {Model} stopped with {StopReason}: {Usage}",
                toolName, model.ModelId, answer.FinishReason, usage.GetProperty("usage").GetRawText());
            // recorded before the answer is checked: a call cut off or filtered is billed too
            await _usage.RecordAsync(model.ModelId, usage, ct);

            if (answer.FinishReason == "length")
                throw new InvalidOperationException("The model ran out of output tokens before it finished. Ask for a smaller draft, or give this task a model with a larger output limit.");
            if (answer.FinishReason == "content_filter")
                throw new InvalidOperationException("The model declined to write this draft. Reword the request.");
            var json = answer.Arguments.Length > 0 ? answer.Arguments.ToString() : FindJson(answer.Content.ToString());
            if (json == null)
                throw new InvalidOperationException("The model answered without returning a draft. Try again or reword the request.");

            JsonElement input;
            try
            {
                input = JsonDocument.Parse(json).RootElement.Clone();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "The {Tool} answer from {Model} is not JSON (finish reason {FinishReason}): {Answer}",
                    toolName, model.ModelId, answer.FinishReason, json.Length <= 2000 ? json : json[..2000] + "...");
                throw new InvalidOperationException("The model's answer could not be read. Try again.");
            }
            try
            {
                return BedrockAiModelClient.RepairStringifiedJson(input, inputSchema);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not repair the {Tool} answer from {Model}; using it as written", toolName, model.ModelId);
                return input;
            }
        }

        private async Task<StreamedCompletion> StreamAsync(AiTaskModel model, JsonObject body, CancellationToken ct)
        {
            var client = _httpClientFactory.CreateClient(nameof(OpenAiModelClient));
            client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
            using var request = new HttpRequestMessage(HttpMethod.Post, model.BaseUrl.TrimEnd('/') + "/chat/completions")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(model.ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", model.ApiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("{Model} at {BaseUrl} answered {Status}: {Error}", model.ModelId, model.BaseUrl, (int)response.StatusCode, error);
                throw new InvalidOperationException($"The model server answered {(int)response.StatusCode} {response.ReasonPhrase}: {Shorten(error)}");
            }

            var answer = new StreamedCompletion();
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            string line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
                answer.Add(line);
            return answer;
        }

        // a model that ignores the tool sometimes writes the JSON in its answer, with or without a code fence
        public static string FindJson(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;
            var start = content.IndexOf('{');
            var end = content.LastIndexOf('}');
            return start >= 0 && end > start ? content[start..(end + 1)] : null;
        }

        private static string Shorten(string text) =>
            string.IsNullOrEmpty(text) || text.Length <= 300 ? text : text[..300] + "...";
    }

    /// <summary>
    /// Puts a streamed chat completion back together: the tool call's arguments, any text, why it stopped and what it used.
    /// The stream is server-sent events, one "data: {json}" line per chunk, ending with "data: [DONE]".
    /// </summary>
    public class StreamedCompletion
    {
        public StringBuilder Arguments { get; } = new StringBuilder();
        public StringBuilder Content { get; } = new StringBuilder();
        public string FinishReason { get; private set; }
        public JsonObject Usage { get; private set; }

        /// <summary>Adds one line of the stream; returns the piece of the tool call's arguments it carried, if any</summary>
        public string Add(string line)
        {
            if (line == null || !line.StartsWith("data:", StringComparison.Ordinal))
                return null;
            var data = line[5..].Trim();
            if (data.Length == 0 || data == "[DONE]")
                return null;
            var chunk = JsonNode.Parse(data)?.AsObject();
            if (chunk == null)
                return null;
            if (chunk["error"] is JsonNode error)
                throw new InvalidOperationException($"The model stopped with an error: {error["message"]?.GetValue<string>() ?? error.ToJsonString()}");
            if (chunk["usage"] is JsonObject usage)
                Usage = usage;
            string piece = null;
            foreach (var choice in chunk["choices"]?.AsArray() ?? new JsonArray())
            {
                if (choice?["finish_reason"] is JsonValue finish && finish.TryGetValue<string>(out var reason))
                    FinishReason = reason;
                var delta = choice?["delta"];
                if (delta?["content"] is JsonValue content && content.TryGetValue<string>(out var text))
                    Content.Append(text);
                // only one tool is offered, so every call's arguments belong to it
                foreach (var call in delta?["tool_calls"]?.AsArray() ?? new JsonArray())
                {
                    if (call?["function"]?["arguments"] is JsonValue arguments && arguments.TryGetValue<string>(out var part) && part.Length > 0)
                    {
                        Arguments.Append(part);
                        piece = (piece ?? "") + part;
                    }
                }
            }
            return piece;
        }

        /// <summary>The answer as the usage records read a Messages API answer: its stop reason and token counts</summary>
        public JsonElement ToUsageMessage()
        {
            int Count(string name) => Usage?[name] is JsonValue value && value.TryGetValue<int>(out var n) ? n : 0;
            var cached = Usage?["prompt_tokens_details"]?["cached_tokens"] is JsonValue c && c.TryGetValue<int>(out var cachedTokens) ? cachedTokens : 0;
            var message = new JsonObject
            {
                ["stop_reason"] = FinishReason switch
                {
                    "tool_calls" => "tool_use",
                    "length" => "max_tokens",
                    "content_filter" => "refusal",
                    "stop" => "end_turn",
                    _ => FinishReason
                },
                ["usage"] = new JsonObject
                {
                    // OpenAI counts cached prompt tokens inside prompt_tokens; the records count them apart, as Anthropic does
                    ["input_tokens"] = Math.Max(0, Count("prompt_tokens") - cached),
                    ["output_tokens"] = Count("completion_tokens"),
                    ["cache_read_input_tokens"] = cached
                }
            };
            return JsonSerializer.SerializeToElement(message);
        }
    }
}
