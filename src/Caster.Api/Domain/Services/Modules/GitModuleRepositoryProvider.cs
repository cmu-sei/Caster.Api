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
    ILockService lockService,
    ModuleSourceReadCache readCache,
    ILogger<GitModuleRepositoryProvider> logger) : IModuleRepositoryProvider
{
    public const string VariablesFileName = "variables.tf.json";
    public const string OutputsFileName = "outputs.tf.json";
    public const string MetadataFileName = "caster.json";

    /// <summary>Temp directories are created under here so a crashed process leaves one obvious tree.</summary>
    public const string TempDirectoryName = "caster-module-sources";

    /// <summary>How long a request waits for another's sync of the same source before serving what is stored.</summary>
    public const int SyncLockTimeoutMilliseconds = 60000;

    /// <summary><see cref="SyncLockTimeoutMilliseconds"/> unless a test needs a timeout it can wait out.</summary>
    public int SyncLockTimeout { get; init; } = SyncLockTimeoutMilliseconds;

    public string Provider => ModuleSourceProviders.Git;

    /// <summary>Shown for a source that has neither tags nor Module roots.</summary>
    public const string NoModulesMessage = "No Modules yet.";

    /// <summary>Shown for a source with Module roots on its default branch but no tag to version them.</summary>
    public const string NoVersionTagsMessage =
        "Has Modules but no version tags, so none can be used. Tag the repository, e.g. v1.0.0.";

    /// <summary>Shown for a source whose tags are gone while Modules read from them earlier are still stored.</summary>
    public static string NoVersionTagsAnyMoreMessage(int stored) =>
        $"No version tags any more; showing the {StoredModules(stored)} stored from earlier.";

    /// <summary>Shown for a source whose tags no longer hold a Module root while Modules read earlier are still stored.</summary>
    public static string TagsNoLongerHoldModulesMessage(int stored) =>
        $"Its tags no longer contain Modules; showing the {StoredModules(stored)} stored from earlier.";

    private static string StoredModules(int stored) => stored == 1 ? "1 Module" : $"{stored} Modules";

    /// <summary>Shown for an untagged source whose HEAD names a branch that does not exist.</summary>
    public const string DefaultBranchUnreadableMessage =
        "Couldn't read the repository's default branch to look for Modules.";

    /// <summary>Shown for a source that has tags but no Module root in any of them.</summary>
    public static string NoModuleRootsMessage(ModuleSourceLayout layout) =>
        layout == ModuleSourceLayout.Subdirectories
            ? $"No Modules yet. Subdirectories layout: add a folder with a {VariablesFileName} or {OutputsFileName} " +
              "(.tf files aren't read yet), then tag the repository."
            : $"No Modules yet. Root layout: add a {VariablesFileName} or {OutputsFileName} at the repository root " +
              "(.tf files aren't read yet), then tag the repository.";

    /// <remarks>
    /// A source with no Module to offer yet - no tags, or tags but no Module
    /// root - is reported as Empty or Warning instead of throwing, because an
    /// organization seeding empty repositories is not a failure. Nothing stored
    /// is touched in those cases, the same as when a sync fails.
    /// </remarks>
    public async Task<ModuleSyncResult> SyncModulesAsync(ModuleSourceOptions source, bool forceUpdate, CancellationToken cancellationToken) =>
        await SyncAsync(source, null, forceUpdate, wholeSource: true, cancellationToken);

    /// <remarks>
    /// The caller named a Module, so a source that has none still throws.
    /// </remarks>
    public async Task<bool> SyncModuleAsync(ModuleSourceOptions source, string providerId, CancellationToken cancellationToken)
    {
        // A single-module request always re-reads, because the caller asked for
        // this one Module by name and expects it refreshed.
        await SyncAsync(
            source,
            string.IsNullOrWhiteSpace(providerId) ? null : providerId.Trim('/'),
            forceUpdate: true,
            wholeSource: false,
            cancellationToken);

        return true;
    }

    private async Task<ModuleSyncResult> SyncAsync(
        ModuleSourceOptions source,
        string subdirectoryFilter,
        bool forceUpdate,
        bool wholeSource,
        CancellationToken cancellationToken)
    {
        // A url with credentials is never cloned, listed, or stored in a
        // UrlLink: everything below would put it somewhere it can leak.
        RefuseUnusableUrl(source);

        // Every module list request syncs, and a page load sends several at
        // once. Unserialized, they each replace the same version rows and all
        // but one fail on rows the others already deleted. Waiting also means
        // the up-to-date check below sees what the other request stored.
        using (var lockResult = await lockService.GetModuleSourceLock(source.Name).LockAsync(SyncLockTimeout))
        {
            if (!lockResult.AcquiredLock)
            {
                logger.LogWarning(
                    "Module source {Source} is still being synced by another request; serving the stored Modules.",
                    source.Name);

                return ModuleSyncResult.NotRead;
            }

            return await SyncLockedAsync(source, subdirectoryFilter, forceUpdate, wholeSource, cancellationToken);
        }
    }

    private void RefuseUnusableUrl(ModuleSourceOptions source)
    {
        var check = ModuleSourceUrl.Check(source.Url);

        if (check != ModuleSourceUrlCheck.Usable)
        {
            logger.LogWarning(
                "Refusing module source {Source}: its url {Reason}.",
                source.Name,
                check == ModuleSourceUrlCheck.Credentials ? "contains credentials" : "is not a usable git url");

            ModuleSourceUrl.EnsureUsable(source.Url);
        }
    }

    private async Task<ModuleSyncResult> SyncLockedAsync(
        ModuleSourceOptions source,
        string subdirectoryFilter,
        bool forceUpdate,
        bool wholeSource,
        CancellationToken cancellationToken)
    {
        var requestTime = DateTime.UtcNow;

        var remote = GitTagParser.ParseRemote(await git.ListRemoteRefsAsync(source.Url, cancellationToken));
        var tags = remote.Tags;

        // With no tags and no HEAD, whether any branch exists is what tells a
        // repository with no commits from one whose HEAD names a missing
        // branch, so it is part of what the read depends on. Listing branches
        // is a second ls-remote, so its answer is kept for as long as the
        // tags and HEAD stay as they are, and asked again only when forced:
        // a branch HEAD does not name, pushed meanwhile, shows on a forced
        // sync or after a restart.
        bool? hasBranches = null;

        if (wholeSource && tags.Count == 0 && remote.HeadSha == null)
        {
            var refsKey = Fingerprint(source, remote, hasBranches: null);

            if (forceUpdate || !readCache.TryGetHasBranches(source.Name, refsKey, out var known))
            {
                known = await git.HasBranchesAsync(source.Url, cancellationToken);
                readCache.RecordHasBranches(source.Name, refsKey, known);
            }

            hasBranches = known;
        }

        var fingerprint = Fingerprint(source, remote, hasBranches);

        // A source with nothing to store is otherwise re-cloned by every
        // Module list, because there are no stored versions to compare.
        if (wholeSource && !forceUpdate && readCache.TryGetEmpty(source.Name, fingerprint, out var cached))
        {
            logger.LogDebug(
                "Module source {Source} is unchanged since it was found to have no Module ({Outcome}); skipping clone.",
                source.Name,
                cached);

            return await EmptyResultAsync(source, cached, cancellationToken);
        }

        if (tags.Count == 0)
        {
            // A repo with no tags has no selectable version, so a Design could
            // never reference it, and nothing is registered. For the whole
            // source that is a state to report, not a failure: Empty for a
            // freshly seeded repository, Warning when Modules are waiting on a
            // tag. A caller that named a Module gets the error.
            if (wholeSource)
            {
                var read = await ReadUntaggedAsync(source, remote, hasBranches == true, cancellationToken);
                readCache.RecordEmpty(source.Name, fingerprint, read);

                return await EmptyResultAsync(source, read, cancellationToken);
            }

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

            readCache.Forget(source.Name);

            return ModuleSyncResult.Ok;
        }

        var drafts = new Dictionary<string, ModuleDraft>(StringComparer.Ordinal);

        // Lowest version first, so the highest tag is read last and its
        // metadata is what the Designer shows. Ordinal order would not do:
        // "v1.10.0" sorts before "v1.9.0", and "network-segment/v2.0.0"
        // before every plain "v" tag.
        var readOrder = ModuleVersionOrder.HighestFirst(tags, t => t, _ => DateTime.MinValue);
        readOrder.Reverse();

        foreach (var tag in readOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A per-module tag only ever describes its own subdirectory, so
            // narrow the read to it, and skip the clone outright when a
            // single-module request asked for a different one.
            var tagModule = GetTagModule(source, tag);
            var filter = subdirectoryFilter;

            if (tagModule != null)
            {
                if (filter != null && !string.Equals(filter, tagModule, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                filter = tagModule;
            }

            await ReadTagAsync(source, tag, filter, drafts, cancellationToken);
        }

        if (drafts.Count == 0)
        {
            if (wholeSource)
            {
                readCache.RecordEmpty(source.Name, fingerprint, EmptyReadOutcome.TaggedNoModuleRoots);

                return await EmptyResultAsync(source, EmptyReadOutcome.TaggedNoModuleRoots, cancellationToken);
            }

            throw new ModuleSyncException(
                $"Module source '{source.Name}' has tags but no Module root containing {VariablesFileName} " +
                $"or {OutputsFileName}" +
                (source.Layout == ModuleSourceLayout.Subdirectories
                    ? " in any immediate subdirectory."
                    : " at the repository root.") +
                (subdirectoryFilter == null ? string.Empty : $" Looked under '{subdirectoryFilter}'."));
        }

        try
        {
            await PersistAsync(drafts.Values, requestTime, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The context is shared across sources in one request. Left
            // tracked, the failed changes are retried by the next source's
            // save and fail that one too.
            db.ChangeTracker.Clear();
            throw;
        }

        if (wholeSource)
        {
            readCache.Forget(source.Name);
        }

        return ModuleSyncResult.Ok;
    }

    /// <summary>
    /// Everything a cached empty read depends on: where and how the source is
    /// read, and the refs that read cloned. A tagged source clones its tags,
    /// so their names and shas count (a force-moved tag is a change) and HEAD
    /// does not (a push to main is not). An untagged source clones the default
    /// branch, so HEAD counts, and with no HEAD whether any branch exists.
    /// </summary>
    private static string Fingerprint(ModuleSourceOptions source, GitRemoteRefs remote, bool? hasBranches) =>
        string.Join(
            "\n",
            remote.Tags.Count > 0
                ? [source.Url, source.Layout.ToString(), "tags", .. remote.TagRefs]
                : [source.Url, source.Layout.ToString(), "head", remote.HeadSha ?? "-", hasBranches?.ToString() ?? "-"]);

    /// <summary>
    /// The state of an empty read, counting the Modules the source still has
    /// stored. Counted on every call, cached read or not, because Modules can
    /// be added or removed between reads. A HEAD that could not be read proves
    /// nothing, so it counts nothing.
    /// </summary>
    private async Task<ModuleSyncResult> EmptyResultAsync(
        ModuleSourceOptions source,
        EmptyReadOutcome outcome,
        CancellationToken cancellationToken)
    {
        if (outcome == EmptyReadOutcome.UntaggedHeadUnreadable)
        {
            return ModuleSyncResult.Warning(DefaultBranchUnreadableMessage);
        }

        var stored = await SourceModules(source).AsNoTracking().CountAsync(cancellationToken);

        return ToResult(source, outcome, stored);
    }

    /// <summary>
    /// Turns an empty read into a state. A source that still has Modules
    /// stored from an earlier read is not "No Modules yet": those are still
    /// listed, so it is a Warning that says where they come from.
    /// </summary>
    private static ModuleSyncResult ToResult(ModuleSourceOptions source, EmptyReadOutcome outcome, int stored)
    {
        if (stored > 0)
        {
            return ModuleSyncResult.Warning(outcome == EmptyReadOutcome.TaggedNoModuleRoots
                ? TagsNoLongerHoldModulesMessage(stored)
                : NoVersionTagsAnyMoreMessage(stored));
        }

        return outcome switch
        {
            EmptyReadOutcome.UntaggedWithModuleRoots => ModuleSyncResult.Warning(NoVersionTagsMessage),
            EmptyReadOutcome.TaggedNoModuleRoots => ModuleSyncResult.Empty(NoModuleRootsMessage(source.Layout)),
            _ => ModuleSyncResult.Empty(NoModulesMessage),
        };
    }

    /// <summary>
    /// What an untagged source holds. No HEAD and no branch is a repository
    /// with no commits, so Empty without a clone. No HEAD but some branch means
    /// HEAD names a missing branch, which proves nothing about Modules.
    /// Otherwise the default branch is cloned to look for Module roots.
    /// </summary>
    private async Task<EmptyReadOutcome> ReadUntaggedAsync(
        ModuleSourceOptions source,
        GitRemoteRefs remote,
        bool hasBranches,
        CancellationToken cancellationToken)
    {
        if (remote.HeadSha == null)
        {
            return hasBranches
                ? EmptyReadOutcome.UntaggedHeadUnreadable
                : EmptyReadOutcome.UntaggedNoModuleRoots;
        }

        return await HasModuleRootsOnDefaultBranchAsync(source, cancellationToken)
            ? EmptyReadOutcome.UntaggedWithModuleRoots
            : EmptyReadOutcome.UntaggedNoModuleRoots;
    }

    /// <summary>
    /// Whether the default branch holds any Module root, to tell an untagged
    /// repository that has Modules waiting from one that is simply empty. One
    /// shallow clone, made only for an untagged source with a readable HEAD,
    /// and then only when its refs have changed since the last read (see
    /// <see cref="ModuleSourceReadCache"/>) or the sync is forced.
    /// </summary>
    private async Task<bool> HasModuleRootsOnDefaultBranchAsync(
        ModuleSourceOptions source,
        CancellationToken cancellationToken)
    {
        var workingDirectory = CreateTempDirectory();

        try
        {
            await git.CloneDefaultBranchAsync(source.Url, workingDirectory, cancellationToken);

            return IODirectory.Exists(workingDirectory) &&
                   EnumerateModuleRoots(source, workingDirectory, subdirectoryFilter: null).Any();
        }
        finally
        {
            TryDeleteDirectory(workingDirectory);
        }
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

                // Later tags overwrite earlier ones; SyncAsync reads the
                // highest last.
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

    /// <summary>
    /// In the Subdirectories layout a tag of the form <c>&lt;subdir&gt;/&lt;version&gt;</c>,
    /// such as <c>network-segment/v1.2.0</c>, versions only the Module in that
    /// subdirectory, so one repository can release its Modules independently.
    /// Returns that subdirectory, or null for a plain tag, which versions every
    /// Module. A slash, not a hyphen, separates the two because Module names
    /// and pre-release versions both contain hyphens. The Root layout has one
    /// Module, so every tag is plain there.
    /// </summary>
    public static string GetTagModule(ModuleSourceOptions source, string tag)
    {
        if (source.Layout != ModuleSourceLayout.Subdirectories)
        {
            return null;
        }

        // Subdirectories are immediate children, so the module name cannot
        // itself contain a slash; anything after the first one is the version.
        var slash = tag.IndexOf('/');

        return slash > 0 && slash < tag.Length - 1 ? tag[..slash] : null;
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
    /// Cheap change check: tags are immutable by convention, so if the tags
    /// stored across this source's Modules are exactly the remote tags, there
    /// is nothing to clone. Costs one ls-remote and one query.
    /// </summary>
    /// <remarks>
    /// Compared as a union, not Module by Module: a Module added in a later tag
    /// never carries the tags before it, and a per-module tag belongs to one
    /// Module only. A new tag, including one for a Module not yet registered,
    /// is missing from the union; a deleted tag is left over in it.
    /// </remarks>
    private async Task<bool> IsUpToDateAsync(
        ModuleSourceOptions source,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken)
    {
        var stored = await SourceModules(source)
            .AsNoTracking()
            .SelectMany(m => m.Versions)
            .Select(v => v.Name)
            .Distinct()
            .ToListAsync(cancellationToken);

        return stored.ToHashSet(StringComparer.Ordinal).SetEquals(tags);
    }

    /// <summary>
    /// The stored Modules of <paramref name="source"/>: its name, or anything
    /// under <c>name/</c>, ignoring case as every source name comparison does.
    /// </summary>
    private IQueryable<Module> SourceModules(ModuleSourceOptions source)
    {
        var name = FoldCase(source.Name);
        var prefix = name + "/";

        return db.Modules.Where(m => m.Path.ToLower() == name || m.Path.ToLower().StartsWith(prefix));
    }

    /// <summary>
    /// The one case folding for source names and the Module Paths built from
    /// them. Queries compare a folded parameter with a column folded by SQL
    /// lower().
    /// </summary>
    private static string FoldCase(string value) => value.ToLowerInvariant();

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
            // By Path ignoring case, as source names are compared: a source
            // renamed only in case updates its Modules instead of storing each
            // again beside the old one. The exact match wins if both exist.
            var lowerPath = FoldCase(draft.Path);
            var module = await db.Modules
                .Where(m => m.Path.ToLower() == lowerPath)
                .OrderBy(m => m.Path == draft.Path ? 0 : 1)
                .FirstOrDefaultAsync(cancellationToken);

            if (module == null)
            {
                module = new Module { Path = draft.Path };
                db.Modules.Add(module);
            }

            module.Path = draft.Path;

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
