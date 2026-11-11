// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Project = Caster.Api.Features.Projects.Project;
using Workspace = Caster.Api.Features.Workspaces.Workspace;
using Caster.Api.Domain.Services.Capacity;
using Caster.Api.Features.Projects;
using Caster.Api.Features.Shared;
using Caster.Api.Features.Workspaces;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using FluentValidation;
using MediatR;

namespace Caster.Api.Features.Capacity;

/// <summary>The four limits, as an admin sets them on a project or workspace. A null clears that limit.</summary>
public abstract class DeploymentLimitsFields : IDeploymentLimits
{
    /// <summary>The most machines. Null for no limit at this level.</summary>
    [DataMember]
    public int? MaxMachines { get; set; }

    /// <summary>The most vCPUs between the machines. Null for no limit at this level.</summary>
    [DataMember]
    public int? MaxVcpu { get; set; }

    /// <summary>The most memory, in GiB, between the machines. Null for no limit at this level.</summary>
    [DataMember]
    public int? MaxMemoryGiB { get; set; }

    /// <summary>The most disk, in GiB, between the machines. Null for no limit at this level.</summary>
    [DataMember]
    public int? MaxDiskGiB { get; set; }

    public void ApplyTo(IDeploymentLimitsTarget target)
    {
        target.MaxMachines = MaxMachines;
        target.MaxVcpu = MaxVcpu;
        target.MaxMemoryGiB = MaxMemoryGiB;
        target.MaxDiskGiB = MaxDiskGiB;
    }
}

public class DeploymentLimitsValidator<T> : AbstractValidator<T> where T : DeploymentLimitsFields
{
    public DeploymentLimitsValidator()
    {
        RuleFor(x => x.MaxMachines).GreaterThanOrEqualTo(0).When(x => x.MaxMachines.HasValue);
        RuleFor(x => x.MaxVcpu).GreaterThanOrEqualTo(0).When(x => x.MaxVcpu.HasValue);
        RuleFor(x => x.MaxMemoryGiB).GreaterThanOrEqualTo(0).When(x => x.MaxMemoryGiB.HasValue);
        RuleFor(x => x.MaxDiskGiB).GreaterThanOrEqualTo(0).When(x => x.MaxDiskGiB.HasValue);
    }
}

/// <summary>
/// Sets a project's limits, which every workspace in it is held to. Only for
/// a caller with the EditProjects system permission: a project's own editors
/// cannot raise its limits.
/// </summary>
public class EditProjectLimits
{
    [DataContract(Name = "EditProjectLimitsCommand")]
    public class Command : DeploymentLimitsFields, IRequest<Project>
    {
        [JsonIgnore]
        public Guid Id { get; set; }
    }

    public class CommandValidator : DeploymentLimitsValidator<Command>;

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext db) : BaseHandler<Command, Project>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.EditProjects], cancellationToken);

        public override async Task<Project> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var project = await db.Projects.FindAsync([request.Id], cancellationToken)
                ?? throw new EntityNotFoundException<Project>();

            request.ApplyTo(project);
            await db.SaveChangesAsync(cancellationToken);
            return project.ToProject();
        }
    }
}

/// <summary>
/// Sets a workspace's own limits. Only for a caller with the EditProjects
/// system permission.
/// </summary>
public class EditWorkspaceLimits
{
    [DataContract(Name = "EditWorkspaceLimitsCommand")]
    public class Command : DeploymentLimitsFields, IRequest<Workspace>
    {
        [JsonIgnore]
        public Guid Id { get; set; }
    }

    public class CommandValidator : DeploymentLimitsValidator<Command>;

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext db) : BaseHandler<Command, Workspace>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.EditProjects], cancellationToken);

        public override async Task<Workspace> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var workspace = await db.Workspaces.FindAsync([request.Id], cancellationToken)
                ?? throw new EntityNotFoundException<Workspace>();

            request.ApplyTo(workspace);
            await db.SaveChangesAsync(cancellationToken);
            return workspace.ToWorkspace();
        }
    }
}

/// <summary>The limits in force for a workspace: the lowest of its own, its project's and the global ones.</summary>
public class GetWorkspaceLimits
{
    public class Query : IRequest<EffectiveLimits>
    {
        public Guid Id { get; set; }
    }

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext db, ICapacityCheckService capacity) : BaseHandler<Query, EffectiveLimits>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Domain.Models.Workspace>(request.Id, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);

        public override async Task<EffectiveLimits> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            if (await db.Workspaces.FindAsync([request.Id], cancellationToken) == null)
                throw new EntityNotFoundException<Workspace>();

            return await capacity.GetWorkspaceLimitsAsync(request.Id, cancellationToken);
        }
    }
}

/// <summary>The limits in force for a project's workspaces that set none of their own: the lower of the project's and the global ones.</summary>
public class GetProjectLimits
{
    public class Query : IRequest<EffectiveLimits>
    {
        public Guid Id { get; set; }
    }

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext db, ICapacityCheckService capacity) : BaseHandler<Query, EffectiveLimits>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Domain.Models.Project>(request.Id, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);

        public override async Task<EffectiveLimits> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            if (await db.Projects.FindAsync([request.Id], cancellationToken) == null)
                throw new EntityNotFoundException<Project>();

            return await capacity.GetProjectLimitsAsync(request.Id, cancellationToken);
        }
    }
}
