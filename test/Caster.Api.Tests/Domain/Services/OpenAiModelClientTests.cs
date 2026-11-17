// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Amazon.BedrockRuntime;
using Caster.Api.Domain.Services;
using Caster.Api.Features.Designs;
using Caster.Api.Features.Files;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Caster.Api.Tests.Domain.Services;

/// <summary>Drafting through an OpenAI-compatible chat completions endpoint, such as the OpenAI API or a vLLM server,
/// and routing each AI task to its own provider and model.</summary>
public class OpenAiModelClientTests
{
    private static readonly AiTaskModel Model = new() { Provider = AiOptions.OpenAi, ModelId = "test-model", BaseUrl = "http://models.test/v1/", ApiKey = "key", MaxTokens = 4000, Effort = "xhigh" };

    /// <summary>The streamed tool call's arguments are put together and the request is the OpenAI tool-calling shape.</summary>
    [Fact]
    public async Task InvokeTool_reads_the_streamed_tool_call()
    {
        var (client, handler) = Client(Stream(
            """{"choices":[{"delta":{"role":"assistant","tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"draft_terraform","arguments":""}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"summary\":\"One web"}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":" server\",\"files\":\"[{\\\"name\\\":\\\"web.tf\\\",\\\"content\\\":\\\"x\\\"}]\"}"}}]}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""",
            """{"choices":[],"usage":{"prompt_tokens":140,"completion_tokens":106,"prompt_tokens_details":{"cached_tokens":40}}}"""));

        var input = await client.InvokeToolAsync(Model, "Draft.", "A web server.", "draft_terraform", "Submit it", Schema(), CancellationToken.None);

        Assert.Equal("One web server", input.GetProperty("summary").GetString());
        // a list sent as a string of JSON is repaired, as for Bedrock
        Assert.Equal("web.tf", input.GetProperty("files")[0].GetProperty("name").GetString());
        var sent = JsonNode.Parse(handler.Bodies.Single());
        Assert.Equal("http://models.test/v1/chat/completions", handler.Uri);
        Assert.Equal("Bearer key", handler.Authorization);
        Assert.Equal("function", sent["tools"][0]["type"].GetValue<string>());
        Assert.Equal("draft_terraform", sent["tools"][0]["function"]["name"].GetValue<string>());
        Assert.Equal("draft_terraform", sent["tool_choice"]["function"]["name"].GetValue<string>());
        Assert.Equal(4000, sent["max_completion_tokens"].GetValue<int>());
        Assert.True(sent["stream"].GetValue<bool>());
        Assert.True(sent["stream_options"]["include_usage"].GetValue<bool>());
        Assert.StartsWith(BedrockAiModelClient.Purpose, sent["messages"][0]["content"].GetValue<string>());
        // OpenAI's efforts stop at high
        Assert.Equal("high", sent["reasoning_effort"].GetValue<string>());
    }

    [Fact]
    public void Usage_is_given_in_the_Messages_API_terms_with_cached_tokens_apart()
    {
        var answer = new StreamedCompletion();
        answer.Add("""data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""");
        answer.Add("""data: {"choices":[],"usage":{"prompt_tokens":140,"completion_tokens":106,"prompt_tokens_details":{"cached_tokens":40}}}""");
        answer.Add("data: [DONE]");

        var message = answer.ToUsageMessage();

        Assert.Equal("tool_use", message.GetProperty("stop_reason").GetString());
        Assert.Equal(100, message.GetProperty("usage").GetProperty("input_tokens").GetInt32());
        Assert.Equal(40, message.GetProperty("usage").GetProperty("cache_read_input_tokens").GetInt32());
        Assert.Equal(106, message.GetProperty("usage").GetProperty("output_tokens").GetInt32());
    }

    /// <summary>A model that answers in text instead of calling the tool still has its JSON read.</summary>
    [Fact]
    public async Task InvokeTool_reads_JSON_written_as_text()
    {
        var (client, _) = Client(Stream(
            """{"choices":[{"delta":{"content":"Here it is:\n```json\n{\"summary\": \"Harbor"}}]}""",
            """{"choices":[{"delta":{"content":" range\", \"files\": []}\n```"},"finish_reason":"stop"}]}"""));

        var input = await client.InvokeToolAsync(Model, "Draft.", "A range.", "draft_terraform", "Submit it", Schema(), CancellationToken.None);

        Assert.Equal("Harbor range", input.GetProperty("summary").GetString());
    }

    /// <summary>An answer cut off at the output limit, one the server filtered, and one with no draft each say what happened.</summary>
    [Theory]
    [InlineData("length", "ran out of output tokens")]
    [InlineData("content_filter", "declined")]
    [InlineData("stop", "without returning a draft")]
    public async Task InvokeTool_says_why_there_is_no_draft(string finishReason, string message)
    {
        var (client, _) = Client(Stream(
            $$$"""{"choices":[{"delta":{"content":"Thinking"},"finish_reason":"{{{finishReason}}}"}],"usage":{"prompt_tokens":10,"completion_tokens":4000}}"""));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.InvokeToolAsync(Model, "Draft.", "A range.", "draft_terraform", "Submit it", Schema(), CancellationToken.None));

        Assert.Contains(message, error.Message);
    }

    [Fact]
    public async Task InvokeTool_says_what_the_server_answered()
    {
        var (client, _) = Client(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"detail":"Model not found"}""") });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.InvokeToolAsync(Model, "Draft.", "A range.", "draft_terraform", "Submit it", Schema(), CancellationToken.None));

        Assert.Contains("404", error.Message);
        Assert.Contains("Model not found", error.Message);
    }

    /// <summary>A model that does not reason refuses reasoning_effort; the request is sent again without it.</summary>
    [Fact]
    public async Task InvokeTool_asks_again_without_reasoning_effort_when_the_model_refuses_it()
    {
        var (client, handler) = Client(
            new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":{"message":"Unrecognized request argument: reasoning_effort"}}""") },
            Stream("""{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"summary\":\"ok\",\"files\":[]}"}}]},"finish_reason":"tool_calls"}]}"""));

        var input = await client.InvokeToolAsync(Model, "Draft.", "A range.", "draft_terraform", "Submit it", Schema(), CancellationToken.None);

        Assert.Equal("ok", input.GetProperty("summary").GetString());
        Assert.Equal(2, handler.Bodies.Count);
        Assert.NotNull(JsonNode.Parse(handler.Bodies[0])["reasoning_effort"]);
        Assert.Null(JsonNode.Parse(handler.Bodies[1])["reasoning_effort"]);
    }

    /// <summary>Every call is recorded against its model, in the Messages API's terms, as a Bedrock call is.</summary>
    [Fact]
    public async Task InvokeTool_records_what_the_call_used()
    {
        var usage = Substitute.For<IAiUsageRecorder>();
        var (client, _) = Client(usage, Stream(
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"summary\":\"ok\",\"files\":[]}"}}]},"finish_reason":"tool_calls"}]}""",
            """{"choices":[],"usage":{"prompt_tokens":140,"completion_tokens":106,"prompt_tokens_details":{"cached_tokens":40}}}"""));

        await client.InvokeToolAsync(Model, "Draft.", "A range.", "draft_terraform", "Submit it", Schema(), CancellationToken.None);

        await usage.Received(1).RecordAsync("test-model", Arg.Is<JsonElement>(m => Reports(m, "tool_use", 100, 106, 40)), Arg.Any<CancellationToken>());
    }

    /// <summary>A call cut off at the output limit returns no draft but is billed, so it is recorded all the same.</summary>
    [Fact]
    public async Task InvokeTool_records_a_call_that_ran_out_of_output_tokens()
    {
        var usage = Substitute.For<IAiUsageRecorder>();
        var (client, _) = Client(usage, Stream(
            """{"choices":[{"delta":{"content":"Thinking"},"finish_reason":"length"}],"usage":{"prompt_tokens":10,"completion_tokens":4000}}"""));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.InvokeToolAsync(Model, "Draft.", "A range.", "draft_terraform", "Submit it", Schema(), CancellationToken.None));

        await usage.Received(1).RecordAsync("test-model", Arg.Is<JsonElement>(m => Reports(m, "max_tokens", 10, 4000, 0)), Arg.Any<CancellationToken>());
    }

    /// <summary>A request the server refused never reached the model; only the call sent again is recorded.</summary>
    [Fact]
    public async Task InvokeTool_records_only_the_call_the_model_answered()
    {
        var usage = Substitute.For<IAiUsageRecorder>();
        var (client, _) = Client(usage,
            new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":{"message":"Unrecognized request argument: reasoning_effort"}}""") },
            Stream("""{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"summary\":\"ok\",\"files\":[]}"}}]},"finish_reason":"tool_calls"}]}"""));

        await client.InvokeToolAsync(Model, "Draft.", "A range.", "draft_terraform", "Submit it", Schema(), CancellationToken.None);

        await usage.Received(1).RecordAsync(Arg.Any<string>(), Arg.Any<JsonElement>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeTool_needs_a_base_url()
    {
        var (client, _) = Client(Stream());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.InvokeToolAsync(new AiTaskModel { Provider = AiOptions.OpenAi, ModelId = "m", MaxTokens = 10 }, "Draft.", "A range.", "draft_terraform", "Submit it", Schema(), CancellationToken.None));

        Assert.Contains("BaseUrl", error.Message);
    }

    /// <summary>Each task goes to its own provider and model, and anything it leaves out is the default's.</summary>
    [Fact]
    public void ModelFor_takes_the_tasks_own_settings_and_the_defaults_for_the_rest()
    {
        var options = new AiOptions
        {
            ModelId = "us.anthropic.claude-sonnet-5-5",
            FallbackModelId = "us.anthropic.claude-opus-5-5",
            Region = "us-east-1",
            MaxTokens = 64000,
            Effort = "high",
            Models = new Dictionary<string, AiTaskModel>
            {
                ["Design"] = new() { ModelId = "us.anthropic.claude-haiku-5-5", Effort = "low" },
                ["terraform"] = new() { Provider = "openai", ModelId = "gpt-oss-120b", BaseUrl = "http://models.test/v1", MaxTokens = 16000 }
            }
        };

        var design = options.ModelFor(AiTasks.Design);
        Assert.Equal(("Bedrock", "us.anthropic.claude-haiku-5-5", "us.anthropic.claude-opus-5-5", "us-east-1", "low"),
            (design.Provider, design.ModelId, design.FallbackModelId, design.Region, design.Effort));
        var terraform = options.ModelFor(AiTasks.Terraform);
        Assert.Equal(("openai", "gpt-oss-120b", 16000, "http://models.test/v1"), (terraform.Provider, terraform.ModelId, terraform.MaxTokens, terraform.BaseUrl));
        // another provider takes nothing of the Bedrock default model
        Assert.Null(terraform.FallbackModelId);
        var brief = options.ModelFor(AiTasks.Brief);
        Assert.Equal(("Bedrock", "us.anthropic.claude-sonnet-5-5", 64000, "high"), (brief.Provider, brief.ModelId, brief.MaxTokens, brief.Effort));
        Assert.Equal("us.anthropic.claude-sonnet-5-5", options.ModelFor(null).ModelId);
    }

    /// <summary>With no task settings every call is the Bedrock default, as before tasks could have their own.</summary>
    [Fact]
    public void ModelFor_without_task_settings_is_the_Bedrock_default()
    {
        var options = new AiOptions { ModelId = "us.anthropic.claude-sonnet-5-5", FallbackModelId = "fb", Region = "us-west-2" };

        var model = options.ModelFor(AiTasks.Terraform);

        Assert.Equal(("Bedrock", "us.anthropic.claude-sonnet-5-5", "fb", "us-west-2", 64000, null),
            (model.Provider, model.ModelId, model.FallbackModelId, model.Region, model.MaxTokens, model.Effort));
    }

    [Fact]
    public async Task Router_sends_a_task_to_its_OpenAI_compatible_endpoint()
    {
        var (openAi, handler) = Client(Stream("""{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"summary\":\"ok\",\"files\":[]}"}}]},"finish_reason":"tool_calls"}]}"""));
        var options = new AiOptions
        {
            Enabled = true,
            ModelId = "us.anthropic.claude-sonnet-5-5",
            Models = new Dictionary<string, AiTaskModel>
            {
                [AiTasks.Brief] = new() { Provider = "OpenAI", ModelId = "local-model", BaseUrl = "http://models.test/v1", MaxTokens = 16000 }
            }
        };
        var task = new AiTaskContext();
        task.Use(AiTasks.Brief);
        var router = new AiModelRouter(options, task, new BedrockAiModelClient(options, Substitute.For<IAiUsageRecorder>(), NullLogger<BedrockAiModelClient>.Instance), openAi);

        var input = await router.InvokeToolAsync("Draft.", "A range.", "draft_terraform", "Submit it", Schema(), CancellationToken.None);

        Assert.Equal("ok", input.GetProperty("summary").GetString());
        var sent = JsonNode.Parse(handler.Bodies.Single());
        Assert.Equal("local-model", sent["model"].GetValue<string>());
        Assert.Equal(16000, sent["max_completion_tokens"].GetValue<int>());
    }

    [Theory]
    [InlineData(false, "us.anthropic.claude-sonnet-5-5", null, null, "not configured")]
    [InlineData(true, "", null, null, "not configured")]
    // an OpenAI task names its own model; it does not take Bedrock's
    [InlineData(true, "us.anthropic.claude-sonnet-5-5", "OpenAI", null, "not configured")]
    [InlineData(true, "us.anthropic.claude-sonnet-5-5", "Gemini", "gemini-pro", "Gemini")]
    public async Task Router_refuses_a_task_it_cannot_send(bool enabled, string modelId, string provider, string taskModelId, string message)
    {
        var (openAi, handler) = Client(Stream());
        var options = new AiOptions
        {
            Enabled = enabled,
            ModelId = modelId,
            Models = new Dictionary<string, AiTaskModel> { [AiTasks.Design] = new() { Provider = provider, ModelId = taskModelId } }
        };
        var task = new AiTaskContext();
        task.Use(AiTasks.Design);
        var router = new AiModelRouter(options, task, new BedrockAiModelClient(options, Substitute.For<IAiUsageRecorder>(), NullLogger<BedrockAiModelClient>.Instance), openAi);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            router.InvokeToolAsync("Draft.", "A range.", "draft_design", "Submit it", Schema(), CancellationToken.None));

        Assert.Contains(message, error.Message);
        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public void Bedrock_body_sends_the_effort_only_when_one_is_set()
    {
        var withEffort = BedrockAiModelClient.BuildBody(new AiTaskModel { MaxTokens = 100, Effort = " Medium " }, "Draft.", "A range.", "draft_terraform", "Submit it", Schema());
        var without = BedrockAiModelClient.BuildBody(new AiTaskModel { MaxTokens = 100 }, "Draft.", "A range.", "draft_terraform", "Submit it", Schema());

        Assert.Equal("medium", withEffort["output_config"]["effort"].GetValue<string>());
        Assert.Equal(100, withEffort["max_tokens"].GetValue<int>());
        Assert.False(without.ContainsKey("output_config"));
        Assert.Equal("auto", without["tool_choice"]["type"].GetValue<string>());
    }

    [Theory]
    [InlineData("output_config: Extra inputs are not permitted", true, true)]
    [InlineData("effort is not supported by this model", true, true)]
    [InlineData("messages: field required", true, false)]
    // without an effort sent, no refusal is about it
    [InlineData("output_config: Extra inputs are not permitted", false, false)]
    public void Bedrock_recognises_a_refused_effort(string error, bool sent, bool refused)
    {
        var body = BedrockAiModelClient.BuildBody(new AiTaskModel { MaxTokens = 100, Effort = sent ? "low" : null }, "Draft.", "A range.", "t", "d", Schema());

        Assert.Equal(refused, BedrockAiModelClient.RefusesEffort(new AmazonBedrockRuntimeException(error), body));
    }

    /// <summary>
    /// A real one-machine Terraform draft from a live OpenAI-compatible endpoint. Runs only where CASTER_LIVE_OPENAI_BASE_URL,
    /// CASTER_LIVE_OPENAI_API_KEY and CASTER_LIVE_OPENAI_MODEL are set, so builds never call out. CASTER_LIVE_OPENAI_OUT names
    /// a file to write the draft to (and what the client logs to the same name with .log), and CASTER_LIVE_OPENAI_EFFORT an effort to send.
    /// </summary>
    [Fact]
    public async Task Live_drafts_one_machine_of_Terraform()
    {
        var baseUrl = Environment.GetEnvironmentVariable("CASTER_LIVE_OPENAI_BASE_URL");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(baseUrl), "No live OpenAI-compatible endpoint is configured.");
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());
        var output = Environment.GetEnvironmentVariable("CASTER_LIVE_OPENAI_OUT");
        var client = new OpenAiModelClient(factory, new AiOptions { TimeoutSeconds = 600 }, Substitute.For<IAiUsageRecorder>(),
            string.IsNullOrEmpty(output) ? NullLogger<OpenAiModelClient>.Instance : new FileLogger(output + ".log"));
        var model = new AiTaskModel
        {
            Provider = AiOptions.OpenAi,
            BaseUrl = baseUrl,
            ApiKey = Environment.GetEnvironmentVariable("CASTER_LIVE_OPENAI_API_KEY"),
            ModelId = Environment.GetEnvironmentVariable("CASTER_LIVE_OPENAI_MODEL"),
            MaxTokens = 16000,
            Effort = Environment.GetEnvironmentVariable("CASTER_LIVE_OPENAI_EFFORT")
        };
        List<AiContextFile> files =
        [
            new(Guid.NewGuid(), "provider.tf", "terraform {\n  required_providers {\n    proxmox = { source = \"bpg/proxmox\" }\n  }\n}\n\nvariable \"node_name\" {\n  type = string\n}\n", "range", false)
        ];

        var watch = Stopwatch.StartNew();
        var input = await client.InvokeToolAsync(model,
            AiTerraformDraftBuilder.BuildSystemPrompt(new List<AiCatalogModule>()),
            AiTerraformDraftBuilder.BuildUserPrompt("One Ubuntu web server VM named web, cloned from Proxmox template vmid 9000, with 2 cores, 4 GiB of memory, on bridge vmbr0.", files, [], null),
            AiTerraformDraftBuilder.ToolName, AiTerraformDraftBuilder.ToolDescription, AiTerraformDraftBuilder.BuildToolSchema(),
            TestContext.Current.CancellationToken);
        watch.Stop();

        var draft = AiTerraformDraftBuilder.Review(input, files);
        if (!string.IsNullOrEmpty(output))
            await System.IO.File.WriteAllTextAsync(output,
                $"elapsed: {watch.Elapsed.TotalSeconds:F1}s\nsummary: {draft.Summary}\nwarnings:\n- {string.Join("\n- ", draft.Warnings)}\n" +
                $"topology machines: {draft.Topology?.Machines.Count.ToString() ?? "none"}\n\n" +
                string.Join("\n", draft.Files.Select(f => $"=== {f.Name} ===\n{f.Content}")),
                TestContext.Current.CancellationToken);
        Assert.NotEmpty(draft.Files);
        // which Proxmox provider's resource the model writes is its own judgement; the answer must be a machine
        Assert.Contains(draft.Files, f => f.Content.Contains("resource \"proxmox_"));
    }

    private static (OpenAiModelClient, Handler) Client(params HttpResponseMessage[] responses) =>
        Client(Substitute.For<IAiUsageRecorder>(), responses);

    private static (OpenAiModelClient, Handler) Client(IAiUsageRecorder usage, params HttpResponseMessage[] responses)
    {
        var handler = new Handler(responses);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        return (new OpenAiModelClient(factory, new AiOptions { TimeoutSeconds = 30 }, usage, NullLogger<OpenAiModelClient>.Instance), handler);
    }

    private static bool Reports(JsonElement message, string stopReason, int input, int output, int cacheRead) =>
        message.GetProperty("stop_reason").GetString() == stopReason
        && message.GetProperty("usage").GetProperty("input_tokens").GetInt32() == input
        && message.GetProperty("usage").GetProperty("output_tokens").GetInt32() == output
        && message.GetProperty("usage").GetProperty("cache_read_input_tokens").GetInt32() == cacheRead;

    private static HttpResponseMessage Stream(params string[] chunks)
    {
        var sb = new StringBuilder();
        foreach (var chunk in chunks)
            sb.Append("data: ").Append(chunk).Append("\n\n");
        sb.Append("data: [DONE]\n\n");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sb.ToString(), Encoding.UTF8, "text/event-stream") };
    }

    private static JsonObject Schema() => JsonNode.Parse("""
        {"type":"object","properties":{"summary":{"type":"string"},
         "files":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"content":{"type":"string"}}}}},
         "required":["summary","files"]}
        """).AsObject();

    /// <summary>What the client logs in the live test, written to a file to read what the model answered</summary>
    private sealed class FileLogger(string path) : ILogger<OpenAiModelClient>
    {
        public IDisposable BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) =>
            System.IO.File.AppendAllText(path, $"{DateTime.Now:O} {logLevel}: {formatter(state, exception)}{(exception == null ? "" : "\n" + exception.Message)}\n");
    }

    private sealed class Handler(HttpResponseMessage[] responses) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        public string Uri { get; private set; }
        public string Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Uri = request.RequestUri.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            Bodies.Add(await request.Content.ReadAsStringAsync(ct));
            return responses[Math.Min(Bodies.Count, responses.Length) - 1];
        }
    }
}
