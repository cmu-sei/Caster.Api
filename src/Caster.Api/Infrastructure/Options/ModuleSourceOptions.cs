// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Caster.Api.Infrastructure.Options;

/// <summary>
/// One place Caster looks for Terraform Modules. Configured under
/// <c>Terraform:ModuleSources</c>, or synthesized from the legacy
/// <c>Terraform:Gitlab*</c> keys so both kinds flow through one dispatcher.
/// </summary>
public class ModuleSourceOptions
{
    /// <summary>
    /// Stable identifier for this source. It prefixes the Path of every Module
    /// discovered here, so Modules from two sources cannot collide, and
    /// renaming a source re-registers its Modules under new Paths.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Shown in the Designer when a discovered Module carries no description of
    /// its own.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Which <see cref="Domain.Services.Modules.IModuleRepositoryProvider"/>
    /// reads this source, matched case insensitively. See
    /// <see cref="ModuleSourceProviders"/>. Defaults to plain git.
    /// </summary>
    public string Provider { get; set; } = ModuleSourceProviders.Git;

    /// <summary>
    /// Clone URL handed to git verbatim, and the URL written into the generated
    /// <c>source = "git::&lt;url&gt;?ref=&lt;tag&gt;"</c>. Any scheme git
    /// understands works; https authenticates through the credential store
    /// already populated by the chart's <c>gitcredentials</c> value, and
    /// file:// needs no credentials at all.
    /// </summary>
    public string Url { get; set; }

    /// <summary>
    /// Whether the repository is one Module or a monorepo of many.
    /// </summary>
    public ModuleSourceLayout Layout { get; set; } = ModuleSourceLayout.Root;

    /// <summary>
    /// A source with no name or no url is treated as absent rather than as an
    /// error, so a half-filled entry in a values file does not break startup.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Url);
}

public static class ModuleSourceProviders
{
    /// <summary>Any repository git can clone. No server API involved.</summary>
    public const string Git = "Git";

    /// <summary>The legacy Gitlab group scrape. Metadata comes from the Gitlab REST API.</summary>
    public const string Gitlab = "Gitlab";
}

public enum ModuleSourceLayout
{
    /// <summary>
    /// The repository root is the Module root. One repository, one Module.
    /// </summary>
    Root = 0,

    /// <summary>
    /// Every immediate subdirectory holding a <c>variables.tf.json</c> or
    /// <c>outputs.tf.json</c> is its own Module root. One clone yields many
    /// Modules, and the generated source address uses Terraform's
    /// <c>//subdir</c> syntax.
    /// </summary>
    Subdirectories = 1,
}
