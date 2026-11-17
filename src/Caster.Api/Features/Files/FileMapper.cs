// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using Riok.Mapperly.Abstractions;

namespace Caster.Api.Features.Files;

/// <summary>Files' in-memory mapping and their create and PUT mappings; PATCH is <see cref="FilePatchMapper"/>.</summary>
/// <remarks>
/// <para>
/// A file is otherwise answered through a projection: <see cref="FileProjectionMapper"/> and
/// <see cref="FileWithContentProjectionMapper"/>, which the request-scoped <see cref="GetFileQuery"/> picks
/// between.
/// </para>
/// <para>
/// The entity's <see cref="Domain.Models.File.DirectoryId"/> and <see cref="Domain.Models.File.WorkspaceId"/>
/// have private setters, which AutoMapper wrote. Mapperly skips a member it cannot set without a
/// diagnostic, so this mapper includes non-public members, and Mapperly sets them through an
/// <c>UnsafeAccessor</c>.
/// </para>
/// </remarks>
[Mapper(IncludedMembers = MemberVisibility.All)]
public static partial class FileMapper
{
    /// <summary>
    /// A file with its content, mapped in memory. Only a directory write maps files this way, and no
    /// directory write loads any, so a directory's files are always answered as an empty list there.
    /// </summary>
    [MapProperty(nameof(@Domain.Models.File.ModifiedBy.Name), nameof(File.ModifiedByName))]
    [MapProperty(nameof(@Domain.Models.File.LockedBy.Name), nameof(File.LockedByName))]
    public static partial File ToFile(this Domain.Models.File source);

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Create.Command.UpdateContent))]
    public static partial Domain.Models.File ToEntity(this Create.Command source);

    /// <summary>The PUT update: every member is written, so a null name or content clears it.</summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Edit.Command.Id))]
    [MapperIgnoreSource(nameof(Edit.Command.UpdateContent))]
    public static partial void ApplyTo(this Edit.Command source, Domain.Models.File target);
}

/// <summary>The PATCH update. Every null in the command is skipped, so an omitted name or content keeps its current value.</summary>
[Mapper(AllowNullPropertyAssignment = false)]
public static partial class FilePatchMapper
{
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(PartialEdit.Command.Id))]
    [MapperIgnoreSource(nameof(PartialEdit.Command.UpdateContent))]
    public static partial void ApplyPatchTo(this PartialEdit.Command source, Domain.Models.File target);
}

// Each projection variant is a mapper of its own, because a projection takes the default mapping of
// its element types and a mapper has one default per type pair. The names read through a user
// navigation are expression-bodied helpers, which Mapperly inlines without a null guard, so the SQL is
// the LEFT JOIN AutoMapper's was and a missing user reads as null.

/// <summary>The projections of a file and of a file version, without their content.</summary>
[Mapper]
public static partial class FileProjectionMapper
{
    public static partial IQueryable<File> ProjectToFile(this IQueryable<Domain.Models.File> query);

    public static partial IQueryable<FileVersion> ProjectToFileVersion(this IQueryable<Domain.Models.FileVersion> query);

    [MapperIgnoreTarget(nameof(File.Content))]
    [MapProperty(nameof(Domain.Models.File.ModifiedBy), nameof(File.ModifiedByName), Use = nameof(MapUserName))]
    [MapProperty(nameof(Domain.Models.File.LockedBy), nameof(File.LockedByName), Use = nameof(MapUserName))]
    private static partial File MapToFile(Domain.Models.File source);

    [MapperIgnoreTarget(nameof(FileVersion.Content))]
    [MapProperty(nameof(Domain.Models.FileVersion.ModifiedBy), nameof(FileVersion.ModifiedByName), Use = nameof(MapUserName))]
    [MapProperty(nameof(Domain.Models.FileVersion.TaggedBy), nameof(FileVersion.TaggedByName), Use = nameof(MapUserName))]
    private static partial FileVersion MapToFileVersion(Domain.Models.FileVersion source);

    private static string MapUserName(Domain.Models.User user) => user.Name;
}

/// <summary>The projections of a file and of a file version, with their content.</summary>
[Mapper]
public static partial class FileWithContentProjectionMapper
{
    public static partial IQueryable<File> ProjectToFileWithContent(this IQueryable<Domain.Models.File> query);

    public static partial IQueryable<FileVersion> ProjectToFileVersionWithContent(this IQueryable<Domain.Models.FileVersion> query);

    [MapProperty(nameof(Domain.Models.File.ModifiedBy), nameof(File.ModifiedByName), Use = nameof(MapUserName))]
    [MapProperty(nameof(Domain.Models.File.LockedBy), nameof(File.LockedByName), Use = nameof(MapUserName))]
    private static partial File MapToFile(Domain.Models.File source);

    [MapProperty(nameof(Domain.Models.FileVersion.ModifiedBy), nameof(FileVersion.ModifiedByName), Use = nameof(MapUserName))]
    [MapProperty(nameof(Domain.Models.FileVersion.TaggedBy), nameof(FileVersion.TaggedByName), Use = nameof(MapUserName))]
    private static partial FileVersion MapToFileVersion(Domain.Models.FileVersion source);

    private static string MapUserName(Domain.Models.User user) => user.Name;
}
