// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Text.Json;
using Caster.Api.Domain.Services;

namespace Caster.Api.Tests.Domain.Services;

/// <summary><c>BedrockAiModelClient.ReadToolInput</c> - turning the model's response into the draft,
/// or into a message the designer can act on.</summary>
public class BedrockAiModelClientTests
{
    [Fact]
    public void ReadToolInput_returns_the_tool_use_input()
    {
        var input = BedrockAiModelClient.ReadToolInput(Parse("""
            {"stop_reason":"tool_use","content":[{"type":"thinking","thinking":""},{"type":"tool_use","name":"draft_design","input":{"name":"Draft"}}]}
            """));

        Assert.Equal("Draft", input.GetProperty("name").GetString());
    }

    [Fact]
    public void ReadToolInput_names_the_category_of_a_refusal()
    {
        var error = Assert.Throws<InvalidOperationException>(() => BedrockAiModelClient.ReadToolInput(Parse("""
            {"stop_reason":"refusal","stop_details":{"type":"refusal","category":"cyber","explanation":null},"content":[]}
            """)));

        Assert.Contains("declined", error.Message);
        Assert.Contains("cyber", error.Message);
    }

    [Fact]
    public void ReadToolInput_explains_a_refusal_without_a_category()
    {
        var error = Assert.Throws<InvalidOperationException>(() => BedrockAiModelClient.ReadToolInput(Parse("""
            {"stop_reason":"refusal","stop_details":null,"content":[]}
            """)));

        Assert.Equal("The model declined to write this draft. Reword the request.", error.Message);
    }

    [Fact]
    public void ReadToolInput_explains_running_out_of_tokens()
    {
        var error = Assert.Throws<InvalidOperationException>(() => BedrockAiModelClient.ReadToolInput(Parse("""
            {"stop_reason":"max_tokens","content":[{"type":"thinking","thinking":""}]}
            """)));

        Assert.Contains("ran out of output tokens", error.Message);
    }

    [Fact]
    public void ReadToolInput_explains_an_answer_with_no_tool()
    {
        var error = Assert.Throws<InvalidOperationException>(() => BedrockAiModelClient.ReadToolInput(Parse("""
            {"stop_reason":"end_turn","content":[{"type":"text","text":"Here is a draft."}]}
            """)));

        Assert.Contains("without returning a draft", error.Message);
    }

    [Fact]
    public void StreamedMessage_joins_the_tool_input_across_deltas()
    {
        var message = Stream(
            """{"type":"message_start","message":{"usage":{"input_tokens":120,"output_tokens":1}}}""",
            """{"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":""}}""",
            """{"type":"content_block_stop","index":0}""",
            """{"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"t1","name":"draft_design","input":{}}}""",
            """{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"name\":\"Dr"}}""",
            """{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"aft\"}"}}""",
            """{"type":"content_block_stop","index":1}""",
            """{"type":"message_delta","delta":{"stop_reason":"tool_use"},"usage":{"output_tokens":42}}""",
            """{"type":"message_stop"}""");

        Assert.Equal("Draft", BedrockAiModelClient.ReadToolInput(message).GetProperty("name").GetString());
        Assert.Equal(120, message.GetProperty("usage").GetProperty("input_tokens").GetInt32());
        Assert.Equal(42, message.GetProperty("usage").GetProperty("output_tokens").GetInt32());
    }

    /// <summary>A call cut off mid-argument is still billed: its usage and stop reason survive, and the reader explains why there is no draft.</summary>
    [Fact]
    public void StreamedMessage_keeps_the_usage_of_a_call_cut_off_mid_argument()
    {
        var message = Stream(
            """{"type":"message_start","message":{"usage":{"input_tokens":900,"output_tokens":1}}}""",
            """{"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"t1","name":"draft_terraform","input":{}}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\"summary\":\"A web ser"}}""",
            """{"type":"message_delta","delta":{"stop_reason":"max_tokens"},"usage":{"output_tokens":4096}}""",
            """{"type":"message_stop"}""");

        Assert.Equal("max_tokens", message.GetProperty("stop_reason").GetString());
        Assert.Equal(900, message.GetProperty("usage").GetProperty("input_tokens").GetInt32());
        Assert.Equal(4096, message.GetProperty("usage").GetProperty("output_tokens").GetInt32());
        var error = Assert.Throws<InvalidOperationException>(() => BedrockAiModelClient.ReadToolInput(message));
        Assert.Contains("ran out of output tokens", error.Message);
    }

    /// <summary>Only a call that stopped for length may have half an argument; any other malformed one is still an error.</summary>
    [Fact]
    public void StreamedMessage_still_rejects_a_malformed_argument_that_did_not_stop_for_length()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => Stream(
            """{"type":"message_start","message":{"usage":{"input_tokens":10,"output_tokens":1}}}""",
            """{"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"t1","name":"draft_terraform","input":{}}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\"summary\":\"A web ser"}}""",
            """{"type":"message_delta","delta":{"stop_reason":"tool_use"},"usage":{"output_tokens":20}}""",
            """{"type":"message_stop"}"""));
    }

    [Fact]
    public void StreamedMessage_keeps_the_refusal_category()
    {
        var message = Stream(
            """{"type":"message_start","message":{"usage":{"input_tokens":120}}}""",
            """{"type":"message_delta","delta":{"stop_reason":"refusal","stop_details":{"type":"refusal","category":"cyber"}},"usage":{"output_tokens":3632}}""");

        var error = Assert.Throws<InvalidOperationException>(() => BedrockAiModelClient.ReadToolInput(message));
        Assert.Contains("cyber", error.Message);
    }

    [Fact]
    public void StreamedMessage_joins_text_across_deltas()
    {
        var message = Stream(
            """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Here is "}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"a draft."}}""",
            """{"type":"message_delta","delta":{"stop_reason":"end_turn"}}""");

        Assert.Equal("Here is a draft.", message.GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void StreamedMessage_raises_an_error_event()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            Stream("""{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}"""));

        Assert.Contains("Overloaded", error.Message);
    }

    [Fact]
    public void RepairStringifiedJson_parses_an_array_sent_as_a_string()
    {
        var input = Parse("""{"name":"Draft","moves":"[{\"moveNumber\":1},{\"moveNumber\":2}]"}""");

        var repaired = BedrockAiModelClient.RepairStringifiedJson(input, Schema());

        Assert.Equal(JsonValueKind.Array, repaired.GetProperty("moves").ValueKind);
        Assert.Equal(2, repaired.GetProperty("moves")[1].GetProperty("moveNumber").GetInt32());
    }

    [Fact]
    public void RepairStringifiedJson_parses_an_object_sent_as_a_string_inside_an_array()
    {
        var input = Parse("""{"moves":["{\"moveNumber\":1}"]}""");

        var repaired = BedrockAiModelClient.RepairStringifiedJson(input, Schema());

        Assert.Equal(1, repaired.GetProperty("moves")[0].GetProperty("moveNumber").GetInt32());
    }

    [Fact]
    public void RepairStringifiedJson_leaves_strings_the_schema_expects()
    {
        var input = Parse("""{"name":"[1,2]","moves":"not json"}""");

        var repaired = BedrockAiModelClient.RepairStringifiedJson(input, Schema());

        Assert.Equal("[1,2]", repaired.GetProperty("name").GetString());
        Assert.Equal("not json", repaired.GetProperty("moves").GetString());
    }

    private static System.Text.Json.Nodes.JsonObject Schema() =>
        System.Text.Json.Nodes.JsonNode.Parse("""
            {"type":"object","properties":{
              "name":{"type":"string"},
              "moves":{"type":"array","items":{"type":"object","properties":{"moveNumber":{"type":"integer"}}}}}}
            """).AsObject();

    private static JsonElement Stream(params string[] events)
    {
        var message = new StreamedMessage();
        foreach (var e in events)
            message.Add(e);
        return message.ToMessage();
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
