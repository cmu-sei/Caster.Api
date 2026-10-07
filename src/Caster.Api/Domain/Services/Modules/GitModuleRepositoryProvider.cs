// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using IODirectory = System.IO.Directory;
using IOFile = System.IO.File;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Caster.Api.Domain.Services.Modules;

/// <summary>
/// Discovers Modules in any repository git can clone. No server API is
/// involved: versions come from tags via <c>ls-remote</c>, and metadata from a
/// shallow checkout of each tag.
/// </summary>
/// <remarks>
/// This is only a metadata scraper. Terraform itself fetches the module at
/// <c>init</c> from the <c>source = "git::&lt;url&gt;?ref=&lt;tag&gt;"</c>
/// address that <see cref="ModuleVersion.ToSnippet"/> emits, so the url stored
/// here is a plain clone url with nothing api-shaped in it.
/// </remarks>
public class GitModuleRepositoryProvider(
    CasterContext db,
    IGitCommandRunner git,
    ILogger<GitModuleRepositoryProvider> logger) : IModuleRepositoryProvider
{
    public const string VariablesFileName = "variables.tf.json";
    public const string OutputsFileName = "outputs.tf.json";
    public const string MetadataFileName = "caster.json";

    /// <summary>Temp directories are created under here so a crashed process leaves one obvious tree.</summary>
    public const string TempDirectoryName = "caster-module-sources";

    public string Provider => ModuleSourceProviders.Git;

    public Task<bool> SyncModulesAsync(ModuleSourceOptions source, bool forceUpdate, CancellationToken cancellationToken) =>
        SyncAsync(source, null, forceUpdate, cancellationToken);

    public Task<bool> SyncModuleAsync(ModuleSourceOptions source, string providerId, CancellationToken cancellationToken) =>
        // A single-module request always re-reads, because the caller asked for
        // this one Module by name and expects it refreshed.
        SyncAsync(source, string.IsNullOrWhiteSpace(providerId) ? null : providerId.Trim('/'), forceUpdate: true, cancellationToken);

    private async Task<bool> SyncAsync(
        ModuleSourceOptions source,
        string subdirectoryFilter,
        bool forceUpdate,
        CancellationToken cancellationToken)
    {
        var requestTime = DateTime.UtcNow;

        var tags = GitTagParser.Parse(await git.ListRemoteTagsAsync(source.Url, cancellationToken));

        if (tags.Count == 0)
        {
            // A repo with no tags has no selectable version, so a Design could
            // never reference it. Saying so beats registering an empty Module.
            throw new ModuleSyncException(
                $"Module source '{source.Name}' has no git tags, so it has no versions Caster can offer. " +
                "Tag the repository, for example 'v1.0.0', and retry.");
        }

        if (!forceUpdate && await IsUpToDateAsync(source, tags, cancellationToken))
        {
            logger.LogDebug(
                "Module source {Source} is unchanged at {TagCount} tags; skipping clone.",
                source.Name,
                tags.Count);

            return true;
        }

        var drafts = new Dictionary<string, ModuleDraft>(StringComparer.Ordinal);

        foreach (var tag in tags)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ReadTagAsync(source, tag, subdirectoryFilter, drafts, cancellationToken);
        }

        if (drafts.Count == 0)
        {
            throw new ModuleSyncException(
                $"Module source '{source.Name}' has tags but no Module root containing {VariablesFileName} " +
                $"or {OutputsFileName}" +
                (source.Layout == ModuleSourceLayout.Subdirectories
                    ? " in any immediate subdirectory."
                    : " at the repository root.") +
                (subdirectoryFilter == null ? string.Empty : $" Looked under '{subdirectoryFilter}'."));
        }

        await PersistAsync(drafts.Values, requestTime, cancellationToken);

        return true;
    }

    /// <summary>
    /// Clones one tag into a throwaway directory, reads every Module root in
    /// it, and deletes the directory. The finally block is the whole point:
    /// a failed clone or an unparseable json file must not leak a temp tree.
    /// </summary>
    private async Task ReadTagAsync(
        ModuleSourceOptions source,
        string tag,
        string subdirectoryFilter,
        Dictionary<string, ModuleDraft> drafts,
        CancellationToken cancellationToken)
    {
        var workingDirectory = CreateTempDirectory();

        try
        {
            await git.CloneTagAsync(source.Url, tag, workingDirectory, cancellationToken);

            var dateCreated = await git.GetHeadCommitDateAsync(workingDirectory, cancellationToken)
                ?? DateTime.UtcNow;

            foreach (var root in EnumerateModuleRoots(source, workingDirectory, subdirectoryFilter))
            {
                if (!drafts.TryGetValue(root.Path, out var draft))
                {
                    draft = new ModuleDraft(root.Path);
                    drafts[root.Path] = draft;
                }

                // Later tags overwrite earlier ones, so the highest tag's
                // metadata is what the Designer shows.
                draft.Name = root.Name;
                draft.Description = root.Description;

                draft.Versions.Add(new Models.ModuleVersion
                {
                    Name = tag,
                    UrlLink = root.UrlLink,
                    DateCreated = dateCreated,
                    Variables = ReadVariables(root),
                    Outputs = ReadOutputs(root),
                });
            }
        }
        finally
        {
            TryDeleteDirectory(workingDirectory);
        }
    }

    private IEnumerable<ModuleRoot> EnumerateModuleRoots(
        ModuleSourceOptions source,
        string workingDirectory,
        string subdirectoryFilter)
    {
        if (source.Layout == ModuleSourceLayout.Root)
        {
            if (subdirectoryFilter != null)
            {
                yield break;
            }

            if (IsModuleRoot(workingDirectory))
            {
                yield return BuildRoot(source, workingDirectory, subdirectory: null);
            }

            yield break;
        }

        // Subdirectories layout. Immediate children only - a Module's own
        // nested directories, such as the catalog's hcl/ mirror, are not
        // Modules, and recursing would find them.
        foreach (var directory in IODirectory
            .EnumerateDirectories(workingDirectory)
            .OrderBy(x => x, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(directory);

            if (name.StartsWith('.'))
            {
                continue;
            }

            if (subdirectoryFilter != null &&
                !string.Equals(name, subdirectoryFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsModuleRoot(directory))
            {
                continue;
            }

            yield return BuildRoot(source, directory, name);
        }
    }

    private static bool IsModuleRoot(string directory) =>
        IOFile.Exists(Path.Combine(directory, VariablesFileName)) ||
        IOFile.Exists(Path.Combine(directory, OutputsFileName));

    private ModuleRoot BuildRoot(ModuleSourceOptions source, string directory, string subdirectory)
    {
        // Terraform's go-getter reads everything after // as a subdirectory of
        // the cloned repo, so a monorepo needs no per-module repository.
        var urlLink = subdirectory == null
            ? source.Url
            : $"{source.Url.TrimEnd('/')}//{subdirectory}";

        var root = new ModuleRoot
        {
            LocalPath = directory,
            // Path is the upsert key. Prefixing with the source name keeps two
            // sources that both contain "network-segment" from colliding.
            Path = subdirectory == null ? source.Name : $"{source.Name}/{subdirectory}",
            Name = subdirectory ?? source.Name,
            Description = source.Description,
            UrlLink = urlLink,
        };

        ApplyMetadata(root);

        return root;
    }

    /// <summary>
    /// Best effort read of caster.json's displayName and description, so a
    /// monorepo's Modules get distinct labels instead of all inheriting the
    /// source description. Nothing else in that file is read: the variable
    /// display and inventory hints it also carries are a separate decision.
    /// </summary>
    private void ApplyMetadata(ModuleRoot root)
    {
        var metadataPath = Path.Combine(root.LocalPath, MetadataFileName);

        if (!IOFile.Exists(metadataPath))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(IOFile.ReadAllBytes(metadataPath));

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (document.RootElement.TryGetProperty("displayName", out var displayName) &&
                displayName.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(displayName.GetString()))
            {
                root.Name = displayName.GetString();
            }

            if (document.RootElement.TryGetProperty("description", out var description) &&
                description.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(description.GetString()))
            {
                root.Description = description.GetString();
            }
        }
        catch (Exception ex)
        {
            // Display metadata is a nicety; a malformed file must not stop a
            // Module whose variables and outputs parse fine.
            logger.LogWarning(
                ex,
                "Ignoring unreadable {MetadataFileName} for Module {ModulePath}.",
                MetadataFileName,
                root.Path);
        }
    }

    private List<Models.ModuleVariable> ReadVariables(ModuleRoot root)
    {
        var path = Path.Combine(root.LocalPath, VariablesFileName);

        if (!IOFile.Exists(path))
        {
            return [];
        }

        try
        {
            return ModuleVariableResponse.GetModuleVariables(IOFile.ReadAllBytes(path));
        }
        catch (JsonException ex)
        {
            throw new ModuleSyncException(
                $"Module '{root.Path}' has an unreadable {VariablesFileName}: {ex.Message}. " +
                "Caster requires Terraform JSON syntax in object form, with no other block types in the file.");
        }
    }

    private List<Models.ModuleOutput> ReadOutputs(ModuleRoot root)
    {
        var path = Path.Combine(root.LocalPath, OutputsFileName);

        if (!IOFile.Exists(path))
        {
            return [];
        }

        try
        {
            return ModuleOutputResponse.GetModuleOutputs(IOFile.ReadAllBytes(path));
        }
        catch (JsonException ex)
        {
            throw new ModuleSyncException(
                $"Module '{root.Path}' has an unreadable {OutputsFileName}: {ex.Message}. " +
                "Caster requires Terraform JSON syntax in object form, with no other block types in the file.");
        }
    }

    /// <summary>
    /// Cheap change check: tags are immutable by convention, so if every Module
    /// already registered for this source carries exactly the remote's tags,
    /// there is nothing to clone. Costs one ls-remote and one query.
    /// </summary>
    private async Task<bool> IsUpToDateAsync(
        ModuleSourceOptions source,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken)
    {
        var prefix = source.Name + "/";

        var existing = await db.Modules
            .AsNoTracking()
            .Where(m => m.Path == source.Name || m.Path.StartsWith(prefix))
            .Select(m => m.Versions.Select(v => v.Name).ToList())
            .ToListAsync(cancellationToken);

        if (existing.Count == 0)
        {
            return false;
        }

        return existing.All(versionNames => versionNames
            .OrderBy(x => x, StringComparer.Ordinal)
            .SequenceEqual(tags, StringComparer.Ordinal));
    }

    /// <summary>
    /// Upserts each Module by Path, then replaces its versions wholesale. Same
    /// shape as the Gitlab sync, so a Module can move between sources without a
    /// migration.
    /// </summary>
    private async Task PersistAsync(
        IEnumerable<ModuleDraft> drafts,
        DateTime requestTime,
        CancellationToken cancellationToken)
    {
        foreach (var draft in drafts)
        {
            // Mutate the tracked entity rather than Update-ing a fresh instance
            // with the same key. Attaching a second instance throws once the
            // first is already tracked, which happens whenever one scope syncs
            // the same Module twice.
            var module = await db.Modules
                .FirstOrDefaultAsync(m => m.Path == draft.Path, cancellationToken);

            if (module == null)
            {
                module = new Module { Path = draft.Path };
                db.Modules.Add(module);
            }

            module.Name = draft.Name;
            module.Description = draft.Description;
            module.DateModified = requestTime;

            await db.SaveChangesAsync(cancellationToken);

            db.ModuleVersions.RemoveRange(
                await db.ModuleVersions
                    .Where(mv => mv.ModuleId == module.Id)
                    .ToListAsync(cancellationToken));

            foreach (var version in draft.Versions)
            {
                version.ModuleId = module.Id;
                db.ModuleVersions.Add(version);
            }

            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Synced Module {ModulePath} with {VersionCount} version(s) from a git module source.",
                draft.Path,
                draft.Versions.Count);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            TempDirectoryName,
            Guid.NewGuid().ToString("N"));

        // git clone wants a non-existent or empty destination, so only the
        // parent is created here.
        IODirectory.CreateDirectory(Path.GetDirectoryName(path));

        return path;
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (IODirectory.Exists(path))
            {
                // A shallow clone's .git objects are read only on some
                // filesystems, so clear the attribute before deleting.
                foreach (var file in IODirectory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    var attributes = IOFile.GetAttributes(file);

                    if (attributes.HasFlag(FileAttributes.ReadOnly))
                    {
                        IOFile.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                    }
                }

                IODirectory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete the temporary module directory {Path}.", path);
        }
    }

    private sealed class ModuleRoot
    {
        public string LocalPath { get; set; }
        public string Path { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string UrlLink { get; set; }
    }

    private sealed class ModuleDraft(string path)
    {
        public string Path { get; } = path;
        public string Name { get; set; }
        public string Description { get; set; }
        public List<Models.ModuleVersion> Versions { get; } = [];
    }
}
