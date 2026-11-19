// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Caster.Api.Domain.Models;
using Riok.Mapperly.Abstractions;

namespace Caster.Api.Features.Projects;

[Mapper]
public static partial class ProjectMapper
{
    public static partial IQueryable<Project> ProjectToProject(this IQueryable<Domain.Models.Project> query);

    public static partial Project ToProject(this Domain.Models.Project source);

    /// <summary>A new project from the create command. A null id leaves the new entity's alone; the handler sets the id and the creation date.</summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial Domain.Models.Project ToEntity(this Create.Command source);

    /// <summary>The PUT update: every member is written, so a null description clears it.</summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(Edit.Command.Id))]
    public static partial void ApplyTo(this Edit.Command source, Domain.Models.Project target);

    public static partial IQueryable<ProjectMembership> ProjectToProjectMembership(this IQueryable<Domain.Models.ProjectMembership> query);

    public static partial ProjectMembership ToProjectMembership(this Domain.Models.ProjectMembership source);

    /// <summary>The import result, with each locked file named by its path. A missing list is answered as an empty one.</summary>
    [MapProperty(nameof(ImportResult.LockedFiles), nameof(Import.ImportProjectResult.LockedFiles), Use = nameof(MapLockedFiles))]
    [MapProperty(nameof(ImportResult.SkippedSettings), nameof(Import.ImportProjectResult.SkippedSettings), Use = nameof(MapStrings))]
    [MapProperty(nameof(ImportResult.Warnings), nameof(Import.ImportProjectResult.Warnings), Use = nameof(MapStrings))]
    public static partial Import.ImportProjectResult ToImportProjectResult(this ImportResult source);

    private static string[] MapLockedFiles(IEnumerable<Domain.Models.File> lockedFiles) =>
        lockedFiles == null ? [] : lockedFiles.Select(x => x.Path).ToArray();

    private static string[] MapStrings(IEnumerable<string> values) => values == null ? [] : values.ToArray();
}
