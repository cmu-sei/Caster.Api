// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// A second host, for the module routes that need a configured module source. The run-wide host
// configures none (the shipped Terraform:GitlabGroupId is null), which every other module test relies on:
// a configured source there would make each GET api/modules sync against the shared outbound stub.

using System.Collections.Generic;
using Caster.Api.Data;

namespace Caster.Api.Tests.Support;

/// <summary>
/// <see cref="CasterAppFactory"/> with the legacy GitLab module source configured: <c>Terraform:GitlabApiUrl</c>
/// (already set by <see cref="TestConfiguration"/>) and <see cref="GroupId"/> as <c>Terraform:GitlabGroupId</c>,
/// which <c>ModuleRepositoryDispatcher</c> needs both of. One per test class (<c>IClassFixture</c>), over a host
/// database of its own; its <see cref="CasterAppFactory.OutboundHttp"/> answers only that class's GitLab calls.
/// </summary>
public sealed class GitlabSourceAppFactory : CasterAppFactory
{
    /// <summary>The GitLab group the source lists, as <c>Terraform:GitlabGroupId</c>.</summary>
    public const int GroupId = 6;

    protected override IDictionary<string, string> Configuration =>
        new Dictionary<string, string>(TestConfiguration.Values) { ["Terraform:GitlabGroupId"] = GroupId.ToString() };

    protected override ITestDatabaseSession<CasterContext> HostDatabase() => DatabaseFixture.GitlabSourceHostDatabase();

    /// <summary>The run-wide host still uses the Terraform directories when this class's host goes.</summary>
    protected override bool RemovesTerraformDirectories => false;
}
