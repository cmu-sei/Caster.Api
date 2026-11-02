// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// A host with AI turned on and the model substituted. The run-wide host leaves AI off, which the AI routes'
// "not enabled" tests rely on, and no test may reach a real model.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Caster.Api.Tests.Support;

/// <summary>
/// <see cref="CasterAppFactory"/> with <c>Ai:Enabled</c> and a model id set, and <see cref="IAiModelClient"/> replaced by
/// <see cref="Model"/>, which answers what a test arranges and records each call. One per test class
/// (<c>IClassFixture</c>), over a host database of its own.
/// </summary>
public sealed class AiAppFactory : CasterAppFactory
{
    public FakeAiModel Model { get; } = new();

    protected override IDictionary<string, string> Configuration =>
        new Dictionary<string, string>(TestConfiguration.Values)
        {
            ["Ai:Enabled"] = "true",
            ["Ai:ModelId"] = "test-model"
        };

    protected override ITestDatabaseSession<CasterContext> HostDatabase() => DatabaseFixture.AiHostDatabase();

    /// <summary>The run-wide host still uses the Terraform directories when this class's host goes.</summary>
    protected override bool RemovesTerraformDirectories => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiModelClient>();
            services.AddScoped<IAiModelClient>(provider => new FakeAiModelClient(Model, provider.GetRequiredService<IAiTaskContext>()));
        });
    }
}

/// <summary>One call to the substituted model: what it was asked, and the task and Directory it was made for</summary>
public record FakeAiCall(string System, string Prompt, string ToolName, JsonObject Schema, string Task, Guid? DirectoryId);

/// <summary>The model of <see cref="AiAppFactory"/>: answers with the JSON a test gives, by Directory, and records each call.</summary>
public sealed class FakeAiModel
{
    private readonly ConcurrentDictionary<Guid, string> _answers = new();

    /// <summary>Every call, in the order made</summary>
    public ConcurrentQueue<FakeAiCall> Calls { get; } = new();

    /// <summary>The tool input the model answers with for calls made in <paramref name="directoryId"/></summary>
    public void Answers(Guid directoryId, string json) => _answers[directoryId] = json;

    internal JsonElement Answer(Guid? directoryId) =>
        directoryId.HasValue && _answers.TryGetValue(directoryId.Value, out var json)
            ? JsonDocument.Parse(json).RootElement.Clone()
            : throw new InvalidOperationException("The test arranged no answer for this Directory.");
}

internal sealed class FakeAiModelClient(FakeAiModel model, IAiTaskContext task) : IAiModelClient
{
    public Task<JsonElement> InvokeToolAsync(string system, string prompt, string toolName, string toolDescription, JsonObject inputSchema, CancellationToken ct)
    {
        model.Calls.Enqueue(new FakeAiCall(system, prompt, toolName, inputSchema, task.Task, task.DirectoryId));
        return Task.FromResult(model.Answer(task.DirectoryId));
    }
}
