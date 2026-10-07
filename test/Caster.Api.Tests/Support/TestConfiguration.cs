// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// Caster's claims cache and IdP groups, plus the Terraform directories, whose shipped empty
// values make the version validators and GET terraform/versions throw.

using System;
using System.Collections.Generic;
using System.IO;

namespace Caster.Api.Tests.Support;

/// <summary>
/// The configuration <see cref="CasterAppFactory"/> layers over the application's own
/// <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// <c>WebApplicationFactory</c> resolves the content root to the <c>Caster.Api</c> project directory, so the
/// shipped configuration is already in force and only keys whose shipped value breaks or weakens a test run
/// belong here. Every entry states which.
/// </remarks>
internal static class TestConfiguration
{
    /// <summary>Everything below lives here, one directory per test process, deleted with the factory.</summary>
    private static readonly string Root = Path.Combine(Path.GetTempPath(), $"caster-tests-{Environment.ProcessId}");

    /// <summary>The Terraform version directories <see cref="TerraformBinaryPath"/> holds.</summary>
    public static readonly string[] TerraformVersions = ["0.12.29", "1.5.7"];

    /// <summary>
    /// Stands for <c>Terraform:BinaryPath</c>: one empty sub-directory per version, which is all
    /// <c>ProcessTerraformService.IsValidVersion</c> and <c>GetVersions</c> read. No binary is ever run.
    /// </summary>
    public static readonly string TerraformBinaryPath = CreateDirectory("binaries", TerraformVersions);

    /// <summary>Stands for <c>Terraform:RootWorkingDirectory</c>, where a workspace's files would be written.</summary>
    public static readonly string TerraformRootWorkingDirectory = CreateDirectory("work", []);

    /// <summary>
    /// Stands for <c>Terraform:GitlabApiUrl</c>, the base address of the "gitlab" client. Shipped empty, which
    /// <c>new Uri("")</c> refuses. Nothing answers here but <see cref="CasterAppFactory.OutboundHttp"/>.
    /// </summary>
    public const string GitlabApiUrl = "https://gitlab.test/api/v4/";

    public static Dictionary<string, string> Values => new()
    {
        // One host serves the whole run, and UserClaimsService caches claims on user id alone, so cached
        // claims would leak across tests: a user whose permissions one test seeds would keep them in the
        // next test that uses the same id.
        ["ClaimsTransformation:EnableCaching"] = "false",

        // Group memberships come from the rows a test seeds, so that one mechanism decides what an actor may
        // do. UserClaimsServiceTests covers reading groups and roles from the token.
        ["ClaimsTransformation:UseGroupsFromIdP"] = "false",

        // The shipped values are empty: Directory.EnumerateDirectories("") throws, so GET
        // terraform/versions would be a 500 and every TerraformVersion on a workspace would be refused.
        ["Terraform:BinaryPath"] = TerraformBinaryPath,
        // The shipped value too, set here so it always names one of TerraformVersions.
        ["Terraform:DefaultVersion"] = TerraformVersions[0],
        ["Terraform:RootWorkingDirectory"] = TerraformRootWorkingDirectory,
        ["Terraform:GitlabApiUrl"] = GitlabApiUrl,
    };

    /// <summary>Deletes what <see cref="TerraformBinaryPath"/> and <see cref="TerraformRootWorkingDirectory"/> created.</summary>
    public static void RemoveTerraformDirectories()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    private static string CreateDirectory(string name, string[] children)
    {
        var path = Path.Combine(Root, name);

        foreach (var child in children)
        {
            Directory.CreateDirectory(Path.Combine(path, child));
        }

        Directory.CreateDirectory(path);

        return path;
    }
}
