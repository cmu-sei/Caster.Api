// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Features.Projects;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Caster.Api.Tests.Features.Projects;

/// <summary>
/// The pure parts of AI usage: reading what a call used, finding a model's price, totalling a project's calls,
/// and a failed write that must not fail the draft.
/// </summary>
[Trait("Category", "Unit")]
public class AiUsagePricingTests
{
    private static readonly AiPrice Sonnet = new() { InputPerMillion = 2 };

    [Theory]
    [InlineData("us.anthropic.claude-sonnet-5-5")]
    [InlineData("us_anthropic_claude_sonnet_5_5")]
    public void PriceFor_matches_the_model_ignoring_punctuation(string key)
    {
        var prices = new Dictionary<string, AiPrice> { [key] = Sonnet };

        Assert.Same(Sonnet, GetAiUsage.PriceFor(prices, "us.anthropic.claude-sonnet-5-5"));
        Assert.Null(GetAiUsage.PriceFor(prices, "us.anthropic.claude-haiku-5-5"));
        Assert.Null(GetAiUsage.PriceFor(prices, null));
    }

    /// <summary>
    /// A price set as the environment variables Ai__Prices__us_anthropic_claude_sonnet_5_5__InputPerMillion and
    /// ..OutputPerMillion binds to that key, prices the Sonnet inference profile's calls, and leaves nothing unpriced.
    /// </summary>
    [Fact]
    public void A_price_set_as_an_environment_variable_estimates_the_cost_of_its_model()
    {
        var options = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Ai:Prices:us_anthropic_claude_sonnet_5_5:InputPerMillion"] = "3",
                ["Ai:Prices:us_anthropic_claude_sonnet_5_5:OutputPerMillion"] = "15"
            })
            .Build()
            .GetSection("Ai")
            .Get<AiOptions>();
        List<AiUsage> rows =
        [
            new() { Task = AiTasks.Terraform, Model = "us.anthropic.claude-sonnet-5-5", InputTokens = 45565, OutputTokens = 39573, DateCreated = DateTime.UtcNow }
        ];

        var summary = GetAiUsage.Summarize(Guid.NewGuid(), "p", rows, options.Prices);

        // 45,565 in at $3 and 39,573 out at $15 a million: $0.136695 + $0.593595
        Assert.Equal(730_290, summary.EstimatedCostMicros);
        Assert.Equal(730_290, Assert.Single(summary.ByTask).EstimatedCostMicros);
        Assert.Empty(summary.Unpriced);
    }

    /// <summary>The usage a Messages API answer reports is read whole, a refusal included.</summary>
    [Fact]
    public void Read_takes_the_tokens_and_stop_reason_from_the_answer()
    {
        var answer = JsonDocument.Parse("""{"stop_reason":"refusal","usage":{"input_tokens":45565,"output_tokens":39573,"cache_read_input_tokens":12,"cache_creation_input_tokens":34}}""").RootElement;

        var usage = AiUsageRecorder.Read("test-model", answer);
        var empty = AiUsageRecorder.Read("test-model", default);

        Assert.Equal((45565, 39573, 12, 34, "refusal"), (usage.InputTokens, usage.OutputTokens, usage.CacheReadTokens, usage.CacheWriteTokens, usage.StopReason));
        Assert.Equal((0, 0, null), (empty.InputTokens, empty.OutputTokens, empty.StopReason));
    }

    /// <summary>A call made with no task named is totalled as other; the rounding is to the nearest millionth.</summary>
    [Fact]
    public void Summarize_rounds_to_millionths_and_puts_an_untagged_call_under_other()
    {
        var prices = new Dictionary<string, AiPrice> { ["m"] = new() { InputPerMillion = 3, OutputPerMillion = 15 } };
        List<AiUsage> rows =
        [
            new() { Task = null, Model = "m", InputTokens = 1, OutputTokens = 0, DateCreated = DateTime.UtcNow },
            new() { Task = AiTasks.Design, Model = "m", InputTokens = 0, OutputTokens = 1, DateCreated = DateTime.UtcNow }
        ];

        var summary = GetAiUsage.Summarize(Guid.NewGuid(), "p", rows, prices);

        // $0.000003 and $0.000015
        Assert.Equal(18, summary.EstimatedCostMicros);
        Assert.Equal([AiTasks.Design, "other"], summary.ByTask.ConvertAll(t => t.Name));
    }

    /// <summary>The call already happened; a record that cannot be written is logged, and the draft goes on.</summary>
    [Fact]
    public async Task Record_that_cannot_be_written_does_not_fail_the_call()
    {
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(_ => throw new InvalidOperationException("no database"));
        var logger = new RecordingLogger<AiUsageRecorder>();
        var task = new AiTaskContext();
        task.Use(AiTasks.Design, Guid.NewGuid());
        var recorder = new AiUsageRecorder(scopes, task, Substitute.For<IIdentityResolver>(), logger);

        await recorder.RecordAsync("test-model", JsonDocument.Parse("""{"usage":{"input_tokens":1}}""").RootElement, CancellationToken.None);

        Assert.Contains(logger.Entries, e => e.Exception is InvalidOperationException);
    }
}
