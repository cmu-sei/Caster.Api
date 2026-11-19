// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Designs;
using Caster.Api.Features.Files;
using Caster.Api.Features.Workspaces;
using Riok.Mapperly.Abstractions;
using DesignView = Caster.Api.Features.Designs.Design;
using FileView = Caster.Api.Features.Files.File;
using WorkspaceView = Caster.Api.Features.Workspaces.Workspace;

namespace Caster.Api.Features.Directories;

/// <summary>Directories' in-memory, create, PUT and import result mappings; PATCH is <see cref="DirectoryPatchMapper"/>.</summary>
/// <remarks>
/// An in-memory map fills the files, workspaces and designs from whatever the context has loaded, which
/// for every directory write is nothing, so a write answers them as empty lists. A read projects instead:
/// see <see cref="DirectoryExtensions.Expand"/>.
/// </remarks>
[Mapper]
public static partial class DirectoryMapper
{
    [MapProperty(nameof(Domain.Models.Directory.Files), nameof(Directory.Files), Use = nameof(MapFiles))]
    [MapProperty(nameof(Domain.Models.Directory.Workspaces), nameof(Directory.Workspaces), Use = nameof(MapWorkspaces))]
    [MapProperty(nameof(Domain.Models.Directory.Designs), nameof(Directory.Designs), Use = nameof(MapDesigns))]
    public static partial Directory ToDirectory(this Domain.Models.Directory source);

    /// <summary>A new directory from the create command. A null id leaves the new entity's alone; the handler sets it either way.</summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial Domain.Models.Directory ToEntity(this Create.Command source);

    /// <summary>The PUT update: every member is written, so a null parent, version, parallelism or threshold clears it.</summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Edit.Command.Id))]
    public static partial void ApplyTo(this Edit.Command source, Domain.Models.Directory target);

    /// <summary>The import result, with each locked file named by its path. A missing list is answered as an empty one.</summary>
    [MapProperty(nameof(ImportResult.LockedFiles), nameof(Import.ImportDirectoryResult.LockedFiles), Use = nameof(MapLockedFiles))]
    [MapProperty(nameof(ImportResult.SkippedSettings), nameof(Import.ImportDirectoryResult.SkippedSettings), Use = nameof(MapStrings))]
    [MapProperty(nameof(ImportResult.Warnings), nameof(Import.ImportDirectoryResult.Warnings), Use = nameof(MapStrings))]
    public static partial Import.ImportDirectoryResult ToImportDirectoryResult(this ImportResult source);

    private static List<FileView> MapFiles(ICollection<Domain.Models.File> files) =>
        files == null ? [] : files.Select(x => x.ToFile()).ToList();

    private static List<WorkspaceView> MapWorkspaces(ICollection<Domain.Models.Workspace> workspaces) =>
        workspaces == null ? [] : workspaces.Select(x => x.ToWorkspace()).ToList();

    private static List<DesignView> MapDesigns(ICollection<Domain.Models.Design> designs) =>
        designs == null ? [] : designs.Select(x => x.ToDesign()).ToList();

    private static string[] MapLockedFiles(IEnumerable<Domain.Models.File> lockedFiles) =>
        lockedFiles == null ? [] : lockedFiles.Select(x => x.Path).ToArray();

    private static string[] MapStrings(IEnumerable<string> values) => values == null ? [] : values.ToArray();
}

/// <summary>
/// The PATCH update. Every null in the command is skipped, so an omitted member keeps its current value.
/// </summary>
[Mapper(AllowNullPropertyAssignment = false)]
public static partial class DirectoryPatchMapper
{
    /// <remarks>
    /// The parent, parallelism and destroy failure threshold are optional values, mapped by hand: a value
    /// is written only when the command carries one that is not null, so an explicit null is skipped like
    /// an omitted member, as AutoMapper's condition did. The handler then writes an explicit null parent
    /// itself, which detaches the directory.
    /// </remarks>
    public static void ApplyPatchTo(this PartialEdit.Command source, Domain.Models.Directory target)
    {
        ApplyNonNullMembers(source, target);

        if (source.ParentId.HasValue && source.ParentId.Value.HasValue)
            target.ParentId = source.ParentId.Value;

        if (source.Parallelism.HasValue && source.Parallelism.Value.HasValue)
            target.Parallelism = source.Parallelism.Value;

        if (source.AzureDestroyFailureThreshold.HasValue && source.AzureDestroyFailureThreshold.Value.HasValue)
            target.AzureDestroyFailureThreshold = source.AzureDestroyFailureThreshold.Value;
    }

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(PartialEdit.Command.Id))]
    [MapperIgnoreSource(nameof(PartialEdit.Command.ParentId))]
    [MapperIgnoreSource(nameof(PartialEdit.Command.Parallelism))]
    [MapperIgnoreSource(nameof(PartialEdit.Command.AzureDestroyFailureThreshold))]
    private static partial void ApplyNonNullMembers(PartialEdit.Command source, Domain.Models.Directory target);
}

// Each projection variant is a mapper of its own, because a projection takes the default mapping of
// its element types and a mapper has one default per type pair. The nested collections are mapped
// through expression-bodied helpers, which Mapperly inlines without the null guard its own collection
// mapping adds (EF Core would load the whole collection to evaluate it). The names read through a user
// navigation are helpers too, inlined without a null guard, so the SQL is the LEFT JOIN AutoMapper's was.

/// <summary>The projection of a directory without its files, workspaces or designs, which are left null.</summary>
[Mapper]
public static partial class DirectoryProjectionMapper
{
    public static partial IQueryable<Directory> ProjectToDirectory(this IQueryable<Domain.Models.Directory> query);

    [MapperIgnoreTarget(nameof(Directory.Files))]
    [MapperIgnoreTarget(nameof(Directory.Workspaces))]
    [MapperIgnoreTarget(nameof(Directory.Designs))]
    private static partial Directory MapToDirectory(Domain.Models.Directory source);
}

/// <summary>The projection of a directory with its files (without their content), its workspaces and its designs (without their modules).</summary>
[Mapper]
public static partial class DirectoryWithRelatedProjectionMapper
{
    public static partial IQueryable<Directory> ProjectToDirectoryWithRelated(this IQueryable<Domain.Models.Directory> query);

    [MapProperty(nameof(Domain.Models.Directory.Files), nameof(Directory.Files), Use = nameof(MapFiles))]
    [MapProperty(nameof(Domain.Models.Directory.Workspaces), nameof(Directory.Workspaces), Use = nameof(MapWorkspaces))]
    [MapProperty(nameof(Domain.Models.Directory.Designs), nameof(Directory.Designs), Use = nameof(MapDesigns))]
    private static partial Directory MapToDirectory(Domain.Models.Directory source);

    [MapperIgnoreTarget(nameof(FileView.Content))]
    [MapProperty(nameof(Domain.Models.File.ModifiedBy), nameof(FileView.ModifiedByName), Use = nameof(MapUserName))]
    [MapProperty(nameof(Domain.Models.File.LockedBy), nameof(FileView.LockedByName), Use = nameof(MapUserName))]
    private static partial FileView MapToFile(Domain.Models.File source);

    private static partial WorkspaceView MapToWorkspace(Domain.Models.Workspace source);

    [MapperIgnoreTarget(nameof(DesignView.Modules))]
    private static partial DesignView MapToDesign(Domain.Models.Design source);

    private static List<FileView> MapFiles(ICollection<Domain.Models.File> files) => files.Select(x => MapToFile(x)).ToList();

    private static List<WorkspaceView> MapWorkspaces(ICollection<Domain.Models.Workspace> workspaces) =>
        workspaces.Select(x => MapToWorkspace(x)).ToList();

    private static List<DesignView> MapDesigns(ICollection<Domain.Models.Design> designs) => designs.Select(x => MapToDesign(x)).ToList();

    private static string MapUserName(User user) => user.Name;
}

/// <summary>The projection of a directory with its files and their content, its workspaces and its designs (without their modules).</summary>
[Mapper]
public static partial class DirectoryWithRelatedAndFileContentProjectionMapper
{
    public static partial IQueryable<Directory> ProjectToDirectoryWithRelatedAndFileContent(this IQueryable<Domain.Models.Directory> query);

    [MapProperty(nameof(Domain.Models.Directory.Files), nameof(Directory.Files), Use = nameof(MapFiles))]
    [MapProperty(nameof(Domain.Models.Directory.Workspaces), nameof(Directory.Workspaces), Use = nameof(MapWorkspaces))]
    [MapProperty(nameof(Domain.Models.Directory.Designs), nameof(Directory.Designs), Use = nameof(MapDesigns))]
    private static partial Directory MapToDirectory(Domain.Models.Directory source);

    [MapProperty(nameof(Domain.Models.File.ModifiedBy), nameof(FileView.ModifiedByName), Use = nameof(MapUserName))]
    [MapProperty(nameof(Domain.Models.File.LockedBy), nameof(FileView.LockedByName), Use = nameof(MapUserName))]
    private static partial FileView MapToFile(Domain.Models.File source);

    private static partial WorkspaceView MapToWorkspace(Domain.Models.Workspace source);

    [MapperIgnoreTarget(nameof(DesignView.Modules))]
    private static partial DesignView MapToDesign(Domain.Models.Design source);

    private static List<FileView> MapFiles(ICollection<Domain.Models.File> files) => files.Select(x => MapToFile(x)).ToList();

    private static List<WorkspaceView> MapWorkspaces(ICollection<Domain.Models.Workspace> workspaces) =>
        workspaces.Select(x => MapToWorkspace(x)).ToList();

    private static List<DesignView> MapDesigns(ICollection<Domain.Models.Design> designs) => designs.Select(x => MapToDesign(x)).ToList();

    private static string MapUserName(User user) => user.Name;
}
