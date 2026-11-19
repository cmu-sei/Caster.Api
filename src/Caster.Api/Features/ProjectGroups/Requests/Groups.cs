// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.ProjectGroups;

/// <summary>Every group to ViewProjects and ManageProjectGroups; others see the groups they're members of.</summary>
public class GetAll
{
    [DataContract(Name = "GetProjectGroupsQuery")]
    public class Query : IRequest<ProjectGroup[]>;

    public class Handler(CasterContext dbContext, ProjectGroupAccess access) : BaseHandler<Query, ProjectGroup[]>
    {
        public override Task<bool> Authorize(Query request, CancellationToken cancellationToken) => Task.FromResult(true);

        public override async Task<ProjectGroup[]> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            if (access.CanSeeAll())
                return await dbContext.GetViews(_ => true, access, cancellationToken);

            var ids = access.MemberGroupIds().ToList();
            return await dbContext.GetViews(x => ids.Contains(x.Id), access, cancellationToken);
        }
    }
}

public class Get
{
    [DataContract(Name = "GetProjectGroupQuery")]
    public class Query : IRequest<ProjectGroup>
    {
        [JsonIgnore]
        public Guid Id { get; set; }
    }

    public class Handler(CasterContext dbContext, ProjectGroupAccess access) : BaseHandler<Query, ProjectGroup>
    {
        public override Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            Task.FromResult(access.CanSee(request.Id));

        public override Task<ProjectGroup> HandleRequest(Query request, CancellationToken cancellationToken) =>
            dbContext.GetView(request.Id, access, cancellationToken);
    }
}

public class Create
{
    [DataContract(Name = "CreateProjectGroupCommand")]
    public class Command : IRequest<ProjectGroup>
    {
        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public string Description { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
    }

    public class Handler(CasterContext dbContext, ProjectGroupAccess access) : BaseHandler<Command, ProjectGroup>
    {
        public override Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            Task.FromResult(access.CanManageGroups());

        public override async Task<ProjectGroup> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var name = request.Name.Trim();
            await dbContext.EnsureUniqueGroupName(name, null, cancellationToken);

            var group = new Domain.Models.ProjectGroup { Name = name, Description = request.Description };
            dbContext.ProjectGroups.Add(group);
            await dbContext.SaveChangesAsync(cancellationToken);

            return await dbContext.GetView(group.Id, access, cancellationToken);
        }
    }
}

public class Edit
{
    [DataContract(Name = "EditProjectGroupCommand")]
    public class Command : IRequest<ProjectGroup>
    {
        [JsonIgnore]
        public Guid Id { get; set; }

        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public string Description { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
    }

    public class Handler(CasterContext dbContext, ProjectGroupAccess access) : BaseHandler<Command, ProjectGroup>
    {
        public override Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            Task.FromResult(access.CanManageGroups());

        public override async Task<ProjectGroup> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var group = await dbContext.ProjectGroups.SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
                ?? throw new EntityNotFoundException<ProjectGroup>();

            var name = request.Name.Trim();
            await dbContext.EnsureUniqueGroupName(name, group.Id, cancellationToken);

            group.Name = name;
            group.Description = request.Description;
            await dbContext.SaveChangesAsync(cancellationToken);

            return await dbContext.GetView(group.Id, access, cancellationToken);
        }
    }
}

/// <summary>
/// Deletes a group. Its projects and hosts stay, ungrouped, and its memberships and links go. They
/// are removed here rather than by the database, so each change raises the events that tell the
/// affected projects' members.
/// </summary>
public class Delete
{
    [DataContract(Name = "DeleteProjectGroupCommand")]
    public class Command : IRequest
    {
        [JsonIgnore]
        public Guid Id { get; set; }
    }

    public class Handler(CasterContext dbContext, ProjectGroupAccess access) : BaseHandler<Command>
    {
        public override Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            Task.FromResult(access.CanManageGroups());

        public override async Task HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var group = await dbContext.ProjectGroups
                .Include(x => x.Projects)
                .Include(x => x.Hosts)
                .Include(x => x.Memberships)
                .Include(x => x.EnvironmentSets)
                .Include(x => x.InventoryConnections)
                .AsSplitQuery()
                .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
                ?? throw new EntityNotFoundException<ProjectGroup>();

            foreach (var project in group.Projects)
                project.ProjectGroupId = null;

            foreach (var host in group.Hosts)
                host.ProjectGroupId = null;

            dbContext.ProjectGroupEnvironmentSets.RemoveRange(group.EnvironmentSets);
            dbContext.ProjectGroupInventoryConnections.RemoveRange(group.InventoryConnections);
            dbContext.ProjectGroupMemberships.RemoveRange(group.Memberships);
            dbContext.ProjectGroups.Remove(group);

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}

internal static class ProjectGroupNames
{
    public static async Task EnsureUniqueGroupName(this CasterContext dbContext, string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var lowered = name.ToLower();

        if (await dbContext.ProjectGroups.AnyAsync(x => x.Name.ToLower() == lowered && x.Id != exceptId, cancellationToken))
            throw new ConflictException($"A project group named \"{name}\" already exists.");
    }
}
