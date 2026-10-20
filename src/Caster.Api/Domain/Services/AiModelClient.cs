// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
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
                ["system"] = system,
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
            var request = new InvokeModelRequest
            {
                ModelId = _options.ModelId,
                ContentType = "application/json",
                Accept = "application/json",
                Body = new MemoryStream(Encoding.UTF8.GetBytes(body.ToJsonString()))
            };
            var response = await client.InvokeModelAsync(request, ct);
            using var document = await JsonDocument.ParseAsync(response.Body, cancellationToken: ct);
            var root = document.RootElement;
            if (root.TryGetProperty("usage", out var usage))
                _logger.LogInformation("AI draft {Tool} on {Model}: {Usage}", toolName, _options.ModelId, usage.GetRawText());
            if (root.TryGetProperty("stop_reason", out var stopReason) && stopReason.GetString() == "max_tokens")
                throw new InvalidOperationException("The model ran out of output tokens before finishing the draft. Ask for a smaller design.");

            var toolUse = root.GetProperty("content").EnumerateArray()
                .FirstOrDefault(c => c.GetProperty("type").GetString() == "tool_use");
            if (toolUse.ValueKind == JsonValueKind.Undefined)
                throw new InvalidOperationException("The model answered without returning a draft. Try again or reword the request.");

            return toolUse.GetProperty("input").Clone();
        }
    }
}
