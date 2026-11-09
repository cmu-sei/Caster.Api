// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Features.EnvironmentSets;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.ProjectGroups;

/// <summary>
/// Puts a project in a group, moving it out of another. Needs the right to manage the project
/// (ManageProject on it, or ManageProjects), so a group manager can't pull in a project and gain
/// ManageProject over it through the group's memberships; and the right to manage the group
/// (ManageProjectGroups or its manager), and the old group if it's moving.
/// </summary>
public class AddProject
{
    [DataContract(Name = "AddProjectToProjectGroupCommand")]
    public class Command : IRequest<ProjectGroup>
    {
        [JsonIgnore]
        public Guid ProjectGroupId { get; set; }

        [JsonIgnore]
        public Guid ProjectId { get; set; }
    }

    public class Handler(CasterContext dbContext, ProjectGroupAccess access) : BaseHandler<Command, ProjectGroup>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken)
        {
            if (!access.CanManage(request.ProjectGroupId) || !access.CanManageProject(request.ProjectId))
                return false;

            var current = await dbContext.Projects.Where(x => x.Id == request.ProjectId)
                .Select(x => x.ProjectGroupId).FirstOrDefaultAsync(cancellationToken);

            return current is not { } old || old == request.ProjectGroupId || access.CanManage(old);
        }

        public override Task<ProjectGroup> HandleRequest(Command request, CancellationToken cancellationToken) =>
            dbContext.WithEnvironmentSetLock(async () =>
            {
                if (!await dbContext.ProjectGroups.AnyAsync(x => x.Id == request.ProjectGroupId, cancellationToken))
                    throw new EntityNotFoundException<ProjectGroup>();

                var project = await dbContext.Projects.SingleOrDefaultAsync(x => x.Id == request.ProjectId, cancellationToken)
                    ?? throw new EntityNotFoundException<Project>();

                if (project.ProjectGroupId != request.ProjectGroupId)
                {
                    var changes = new ProposedSetChanges();
                    changes.ProjectGroupChanges[project.Id] = request.ProjectGroupId;
                    await dbContext.EnsureNoConflicts(changes, cancellationToken);

                    project.ProjectGroupId = request.ProjectGroupId;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                return await dbContext.GetView(request.ProjectGroupId, access, cancellationToken);
            }, cancellationToken);
    }
}

/// <summary>
/// Takes a project out of its group. Only ManageProjectGroups holders and the group's managers can:
/// a project's own managers can't leave the group they were put in.
/// </summary>
public class RemoveProject
{
    [DataContract(Name = "RemoveProjectFromProjectGroupCommand")]
    public class Command : IRequest<ProjectGroup>
    {
        [JsonIgnore]
        public Guid ProjectGroupId { get; set; }

        [JsonIgnore]
        public Guid ProjectId { get; set; }
    }

    public class Handler(CasterContext dbContext, ProjectGroupAccess access) : BaseHandler<Command, ProjectGroup>
    {
        public override Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            Task.FromResult(access.CanManage(request.ProjectGroupId));

        public override async Task<ProjectGroup> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var project = await dbContext.Projects.SingleOrDefaultAsync(
                    x => x.Id == request.ProjectId && x.ProjectGroupId == request.ProjectGroupId, cancellationToken)
                ?? throw new EntityNotFoundException<Project>("The project is not in this group.");

            project.ProjectGroupId = null;
            await dbContext.SaveChangesAsync(cancellationToken);

            return await dbContext.GetView(request.ProjectGroupId, access, cancellationToken);
        }
    }
}

/// <summary>Gives an Environment Set to every project in a group. Needs ManageEnvironmentSets: group managers can't.</summary>
public class AssignSet
{
    [DataContract(Name = "AssignEnvironmentSetToProjectGroupCommand")]
    public class Command : IRequest<ProjectGroup>
    {
        [JsonIgnore]
        public Guid ProjectGroupId { get; set; }

        [JsonIgnore]
        public Guid EnvironmentSetId { get; set; }
    }

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext, ProjectGroupAccess access) : BaseHandler<Command, ProjectGroup>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageEnvironmentSets], cancellationToken);

        public override Task<ProjectGroup> HandleRequest(Command request, CancellationToken cancellationToken) =>
            dbContext.WithEnvironmentSetLock(async () =>
            {
                if (!await dbContext.ProjectGroups.AnyAsync(x => x.Id == request.ProjectGroupId, cancellationToken))
                    throw new EntityNotFoundException<ProjectGroup>();

                var setName = await dbContext.EnvironmentSets.Where(x => x.Id == request.EnvironmentSetId)
                    .Select(x => x.Name).FirstOrDefaultAsync(cancellationToken)
                    ?? throw new EntityNotFoundException<Domain.Models.EnvironmentSet>();

                if (await dbContext.ProjectGroupEnvironmentSets.AnyAsync(
                        x => x.ProjectGroupId == request.ProjectGroupId && x.EnvironmentSetId == request.EnvironmentSetId, cancellationToken))
                    throw new ConflictException($"Environment Set \"{setName}\" is already assigned to this group.");

                var changes = new ProposedSetChanges();
                changes.GroupAssignments.Add((request.ProjectGroupId, request.EnvironmentSetId));
                await dbContext.EnsureNoConflicts(changes, cancellationToken);

                dbContext.ProjectGroupEnvironmentSets.Add(new ProjectGroupEnvironmentSet
                {
                    ProjectGroupId = request.ProjectGroupId,
                    EnvironmentSetId = request.EnvironmentSetId
                });
                await dbContext.SaveChangesAsync(cancellationToken);

                return await dbContext.GetView(request.ProjectGroupId, access, cancellationToken);
            }, cancellationToken);
    }
}

public class UnassignSet
{
    [DataContract(Name = "UnassignEnvironmentSetFromProjectGroupCommand")]
    public class Command : IRequest<ProjectGroup>
    {
        [JsonIgnore]
        public Guid ProjectGroupId { get; set; }

        [JsonIgnore]
        public Guid EnvironmentSetId { get; set; }
    }

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext, ProjectGroupAccess access) : BaseHandler<Command, ProjectGroup>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageEnvironmentSets], cancellationToken);

        public override async Task<ProjectGroup> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var link = await dbContext.ProjectGroupEnvironmentSets.SingleOrDefaultAsync(
                    x => x.ProjectGroupId == request.ProjectGroupId && x.EnvironmentSetId == request.EnvironmentSetId, cancellationToken)
                ?? throw new EntityNotFoundException<ProjectGroupEnvironmentSet>();

            dbContext.ProjectGroupEnvironmentSets.Remove(link);
            await dbContext.SaveChangesAsync(cancellationToken);

            return await dbContext.GetView(request.ProjectGroupId, access, cancellationToken);
        }
    }
}

/// <summary>Shows a connection's inventory to every project in a group. Needs ManageInfrastructure.</summary>
public class AssignConnection
{
    [DataContract(Name = "AssignInventoryConnectionToProjectGroupCommand")]
    public class Command : IRequest<ProjectGroup>
    {
        [JsonIgnore]
        public Guid ProjectGroupId { get; set; }

        [JsonIgnore]
        public string ConnectionName { get; set; }
    }

    public class Handler(
        ICasterAuthorizationService authorizationService,
        CasterContext dbContext,
        IInfrastructureConnectionCatalog catalog,
        ProjectGroupAccess access) : BaseHandler<Command, ProjectGroup>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageInfrastructure], cancellationToken);

        public override async Task<ProjectGroup> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            if (!await dbContext.ProjectGroups.AnyAsync(x => x.Id == request.ProjectGroupId, cancellationToken))
                throw new EntityNotFoundException<ProjectGroup>();

            var connection = catalog.Find(request.ConnectionName)
                ?? throw new EntityNotFoundException<Inventory.InventoryConnection>($"Inventory connection '{request.ConnectionName}' is not configured.");
            var normalized = ProjectInventoryConnection.Normalize(connection.Name);

            if (!await dbContext.ProjectGroupInventoryConnections.AnyAsync(
                    x => x.ProjectGroupId == request.ProjectGroupId && x.NormalizedConnectionName == normalized, cancellationToken))
            {
                dbContext.ProjectGroupInventoryConnections.Add(new ProjectGroupInventoryConnection
                {
                    ProjectGroupId = request.ProjectGroupId,
                    ConnectionName = connection.Name,
                    NormalizedConnectionName = normalized
                });
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return await dbContext.GetView(request.ProjectGroupId, access, cancellationToken);
        }
    }
}

public class UnassignConnection
{
    [DataContract(Name = "UnassignInventoryConnectionFromProjectGroupCommand")]
    public class Command : IRequest<ProjectGroup>
    {
        [JsonIgnore]
        public Guid ProjectGroupId { get; set; }

        [JsonIgnore]
        public string ConnectionName { get; set; }
    }

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext, ProjectGroupAccess access) : BaseHandler<Command, ProjectGroup>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageInfrastructure], cancellationToken);

        public override async Task<ProjectGroup> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var normalized = ProjectInventoryConnection.Normalize(request.ConnectionName);
            var link = await dbContext.ProjectGroupInventoryConnections.SingleOrDefaultAsync(
                    x => x.ProjectGroupId == request.ProjectGroupId && x.NormalizedConnectionName == normalized, cancellationToken)
                ?? throw new EntityNotFoundException<ProjectGroupInventoryConnection>();

            dbContext.ProjectGroupInventoryConnections.Remove(link);
            await dbContext.SaveChangesAsync(cancellationToken);

            return await dbContext.GetView(request.ProjectGroupId, access, cancellationToken);
        }
    }
}

/// <summary>Sets the partition a group's projects acquire VLANs from when they have none. Needs ManageVLANs.</summary>
public class SetPartition
{
    [DataContract(Name = "SetProjectGroupPartitionCommand")]
    public class Command : IRequest<ProjectGroup>
    {
        [JsonIgnore]
        public Guid ProjectGroupId { get; set; }

        /// <summary>The partition, or null for none.</summary>
        [DataMember]
        public Guid? PartitionId { get; set; }
    }

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext, ProjectGroupAccess access) : BaseHandler<Command, ProjectGroup>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageVLANs], cancellationToken);

        public override async Task<ProjectGroup> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var group = await dbContext.ProjectGroups.SingleOrDefaultAsync(x => x.Id == request.ProjectGroupId, cancellationToken)
                ?? throw new EntityNotFoundException<ProjectGroup>();

            if (request.PartitionId.HasValue && !await dbContext.Partitions.AnyAsync(x => x.Id == request.PartitionId, cancellationToken))
                throw new EntityNotFoundException<Partition>();

            group.PartitionId = request.PartitionId;
            await dbContext.SaveChangesAsync(cancellationToken);

            return await dbContext.GetView(group.Id, access, cancellationToken);
        }
    }
}
