// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// Hosts with module sources configured, for the Module Scope tests that need the Shared flag or the
// RestrictUnassigned setting. The run-wide host configures no source, which every other module test relies on.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Services.Modules;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Caster.Api.Tests.Support;

/// <summary>
/// <see cref="CasterAppFactory"/> with three git sources: <c>cmu</c> and <c>sei</c> (not shared) and <c>stock</c>
/// (<c>Shared</c>), and <c>Terraform:ModuleScopes:RestrictUnassigned</c> off. Git is replaced by a runner that
/// always fails, so the sync every module list starts fails (and is logged, which the API tolerates) and the
/// seeded Modules stand as they are.
/// </summary>
public class SharedSourcesAppFactory : CasterAppFactory
{
    protected virtual bool RestrictUnassigned => false;

    protected override IDictionary<string, string> Configuration => new Dictionary<string, string>(TestConfiguration.Values)
    {
        ["Terraform:ModuleSources:0:Name"] = "cmu",
        ["Terraform:ModuleSources:0:Url"] = "file:///does-not-exist/cmu.git",
        ["Terraform:ModuleSources:1:Name"] = "sei",
        ["Terraform:ModuleSources:1:Url"] = "file:///does-not-exist/sei.git",
        ["Terraform:ModuleSources:2:Name"] = "stock",
        ["Terraform:ModuleSources:2:Url"] = "file:///does-not-exist/stock.git",
        ["Terraform:ModuleSources:2:Shared"] = "true",
        ["Terraform:ModuleScopes:RestrictUnassigned"] = RestrictUnassigned ? "true" : "false",
    };

    protected override ITestDatabaseSession<CasterContext> HostDatabase() => DatabaseFixture.SharedSourcesHostDatabase();

    protected override bool RemovesTerraformDirectories => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGitCommandRunner>();
            services.AddSingleton<IGitCommandRunner, UnreachableGitCommandRunner>();
        });
    }

    private sealed class UnreachableGitCommandRunner : IGitCommandRunner
    {
        public Task<IReadOnlyList<string>> ListRemoteTagsAsync(string url, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Git is not reachable in this test host.");

        public Task CloneTagAsync(string url, string tag, string destinationPath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Git is not reachable in this test host.");

        public Task<DateTime?> GetHeadCommitDateAsync(string repositoryPath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Git is not reachable in this test host.");
    }
}

/// <summary><see cref="SharedSourcesAppFactory"/> with <c>Terraform:ModuleScopes:RestrictUnassigned</c> on.</summary>
public sealed class RestrictingSharedSourcesAppFactory : SharedSourcesAppFactory
{
    protected override bool RestrictUnassigned => true;

    protected override ITestDatabaseSession<CasterContext> HostDatabase() => DatabaseFixture.RestrictingHostDatabase();
}
