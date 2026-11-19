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
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.ProjectGroups;

/// <summary>A group's memberships: to ViewProjects, ManageProjectGroups and the group's managers.</summary>
public class GetMemberships
{
    [DataContract(Name = "GetProjectGroupMembershipsQuery")]
    public class Query : IRequest<ProjectGroupMembership[]>
    {
        [JsonIgnore]
        public Guid ProjectGroupId { get; set; }
    }

    public class Handler(CasterContext dbContext, ProjectGroupAccess access) : BaseHandler<Query, ProjectGroupMembership[]>
    {
        public override Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            Task.FromResult(access.CanListMemberships(request.ProjectGroupId));

        public override Task<ProjectGroupMembership[]> HandleRequest(Query request, CancellationToken cancellationToken) =>
            dbContext.GetMembershipViews(request.ProjectGroupId, cancellationToken);
    }
}

/// <summary>
/// Gives a user or user group a project role in every project of the group. ManageProjectGroups
/// holders and the group's managers can grant any role, the manager role included, as user-group
/// managers can.
/// </summary>
public class CreateMembership
{
    [DataContract(Name = "CreateProjectGroupMembershipCommand")]
    public class Command : IRequest<ProjectGroupMembership[]>
    {
        [JsonIgnore]
        public Guid ProjectGroupId { get; set; }

        [DataMember]
        public Guid? UserId { get; set; }

        [DataMember]
        public Guid? GroupId { get; set; }

        /// <summary>The project role. Without one, the seeded Member role.</summary>
        [DataMember]
        public Guid? RoleId { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator() =>
            RuleFor(x => x).Must(x => x.UserId.HasValue != x.GroupId.HasValue)
                .WithMessage("A membership is for a user or a group, not both.");
    }

    public class Handler(CasterContext dbContext, ProjectGroupAccess access) : BaseHandler<Command, ProjectGroupMembership[]>
    {
        public override Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            Task.FromResult(access.CanManage(request.ProjectGroupId));

        public override async Task<ProjectGroupMembership[]> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            if (!await dbContext.ProjectGroups.AnyAsync(x => x.Id == request.ProjectGroupId, cancellationToken))
                throw new EntityNotFoundException<ProjectGroup>();

            var roleId = request.RoleId ?? ProjectRoleDefaults.ProjectMemberRoleId;

            if (!await dbContext.ProjectRoles.AnyAsync(x => x.Id == roleId, cancellationToken))
                throw new EntityNotFoundException<ProjectRole>();

            if (request.UserId.HasValue && !await dbContext.Users.AnyAsync(x => x.Id == request.UserId, cancellationToken))
                throw new EntityNotFoundException<User>();

            if (request.GroupId.HasValue && !await dbContext.Groups.AnyAsync(x => x.Id == request.GroupId, cancellationToken))
                throw new EntityNotFoundException<Group>();

            if (await dbContext.ProjectGroupMemberships.AnyAsync(
                    x => x.ProjectGroupId == request.ProjectGroupId && x.UserId == request.UserId && x.GroupId == request.GroupId, cancellationToken))
                throw new ConflictException("That membership already exists.");

            dbContext.ProjectGroupMemberships.Add(new Domain.Models.ProjectGroupMembership
            {
                ProjectGroupId = request.ProjectGroupId,
                UserId = request.UserId,
                GroupId = request.GroupId,
                RoleId = roleId
            });
            await dbContext.SaveChangesAsync(cancellationToken);

            return await dbContext.GetMembershipViews(request.ProjectGroupId, cancellationToken);
        }
    }
}

public class EditMembership
{
    [DataContract(Name = "EditProjectGroupMembershipCommand")]
    public class Command : IRequest<ProjectGroupMembership[]>
    {
        [JsonIgnore]
        public Guid Id { get; set; }

        [DataMember]
        public Guid RoleId { get; set; }
    }

    public class Handler(CasterContext dbContext, ProjectGroupAccess access) : BaseHandler<Command, ProjectGroupMembership[]>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await dbContext.ProjectGroupMemberships.Where(x => x.Id == request.Id).Select(x => (Guid?)x.ProjectGroupId)
                .FirstOrDefaultAsync(cancellationToken) is { } groupId && access.CanManage(groupId);

        public override async Task<ProjectGroupMembership[]> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var membership = await dbContext.ProjectGroupMemberships.SingleAsync(x => x.Id == request.Id, cancellationToken);

            if (!await dbContext.ProjectRoles.AnyAsync(x => x.Id == request.RoleId, cancellationToken))
                throw new EntityNotFoundException<ProjectRole>();

            membership.RoleId = request.RoleId;
            await dbContext.SaveChangesAsync(cancellationToken);

            return await dbContext.GetMembershipViews(membership.ProjectGroupId, cancellationToken);
        }
    }
}

public class DeleteMembership
{
    [DataContract(Name = "DeleteProjectGroupMembershipCommand")]
    public class Command : IRequest<ProjectGroupMembership[]>
    {
        [JsonIgnore]
        public Guid Id { get; set; }
    }

    public class Handler(CasterContext dbContext, ProjectGroupAccess access) : BaseHandler<Command, ProjectGroupMembership[]>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await dbContext.ProjectGroupMemberships.Where(x => x.Id == request.Id).Select(x => (Guid?)x.ProjectGroupId)
                .FirstOrDefaultAsync(cancellationToken) is { } groupId && access.CanManage(groupId);

        public override async Task<ProjectGroupMembership[]> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var membership = await dbContext.ProjectGroupMemberships.SingleAsync(x => x.Id == request.Id, cancellationToken);

            dbContext.ProjectGroupMemberships.Remove(membership);
            await dbContext.SaveChangesAsync(cancellationToken);

            return await dbContext.GetMembershipViews(membership.ProjectGroupId, cancellationToken);
        }
    }
}
