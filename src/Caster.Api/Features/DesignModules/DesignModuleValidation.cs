// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Services.ModuleScopes;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.DesignModules;

public static class DesignModuleValidation
{
    // Terraform block labels, such as a module instance name.
    public const string TerraformNamePattern = "^[a-zA-Z_][a-zA-Z0-9_-]*$";
    public const string TerraformNameMessage =
        "Name must start with a letter or underscore and contain only letters, digits, underscores and dashes";

    /// <summary>
    /// Refuses a Module outside the Module Scopes of the Design's project, which limit the modules it may use.
    /// </summary>
    public static async Task EnsureModuleAllowed(
        CasterContext dbContext,
        IModuleAccessService moduleAccess,
        Guid designId,
        Guid moduleId,
        CancellationToken cancellationToken)
    {
        var projectId = await dbContext.Designs
            .Where(x => x.Id == designId)
            .Select(x => x.Directory.ProjectId)
            .SingleAsync(cancellationToken);

        var path = await dbContext.Modules
            .Where(x => x.Id == moduleId)
            .Select(x => x.Path)
            .SingleAsync(cancellationToken);

        if (!(await moduleAccess.ForProject(projectId, cancellationToken)).Allows(path))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(DesignModuleFields.ModuleId)] = [$"The module '{path}' is outside the Module Scopes of this project, which limit the modules it may use."]
            });
        }
    }

    /// <summary>
    /// A Design is turned straight into a Workspace file, so a DesignModule must have a
    /// name unique in its Design, name an existing version and give every required
    /// variable a value. Checked against the
    /// values as they will be saved, since Edit and AddOrUpdateValues only send changes.
    /// </summary>
    public static async Task EnsureComplete(CasterContext dbContext, Domain.Models.DesignModule designModule, CancellationToken cancellationToken)
    {
        // Each module becomes a block in the same generated file, where a repeated
        // name is a duplicate module error.
        var nameTaken = await dbContext.DesignModules
            .AnyAsync(m => m.DesignId == designModule.DesignId &&
                           m.Id != designModule.Id &&
                           m.Name == designModule.Name, cancellationToken);

        if (nameTaken)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(designModule.Name)] = [$"This design already has a module named '{designModule.Name}'"]
            });
        }

        var version = await dbContext.ModuleVersions
            .Where(v => v.ModuleId == designModule.ModuleId && v.Name == designModule.ModuleVersion)
            .FirstOrDefaultAsync(cancellationToken);

        if (version == null)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(designModule.ModuleVersion)] = [$"Module version '{designModule.ModuleVersion}' does not exist"]
            });
        }

        var missing = version.MissingRequiredVariables(designModule.Values);

        if (missing.Length > 0)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(designModule.Values)] = [$"Required values are missing: {string.Join(", ", missing)}"]
            });
        }
    }
}
