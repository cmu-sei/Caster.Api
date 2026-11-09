// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.ProjectGroups;

/// <summary>A project group: its projects and what it carries.</summary>
public class ProjectGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public Guid? PartitionId { get; set; }
    public ProjectGroupItem[] Projects { get; set; } = [];
    public ProjectGroupItem[] EnvironmentSets { get; set; } = [];

    /// <summary>The inventory connections every project in the group sees, by name.</summary>
    public string[] InventoryConnections { get; set; } = [];

    public ProjectGroupItem[] Hosts { get; set; } = [];

    /// <summary>Whether the caller can manage the group's memberships and projects.</summary>
    public bool CanManage { get; set; }
}

/// <summary>Something a group holds, by id and name.</summary>
public class ProjectGroupItem
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}

public class ProjectGroupMembership
{
    public Guid Id { get; set; }
    public Guid ProjectGroupId { get; set; }
    public Guid? UserId { get; set; }
    public string UserName { get; set; }
    public Guid? GroupId { get; set; }
    public string GroupName { get; set; }
    public Guid RoleId { get; set; }
    public string RoleName { get; set; }
}

/// <summary>Who may do what to a project group, from the caller's claims.</summary>
public class ProjectGroupAccess(ICasterAuthorizationService authorizationService)
{
    private bool Has(SystemPermission permission) => authorizationService.GetSystemPermissions().Contains(permission);

    /// <summary>ViewProjects and ManageProjectGroups see every group.</summary>
    public bool CanSeeAll() => Has(SystemPermission.ViewProjects) || Has(SystemPermission.ManageProjectGroups);

    public bool CanManageGroups() => Has(SystemPermission.ManageProjectGroups);

    public bool CanSee(Guid groupId) =>
        CanSeeAll() || authorizationService.GetProjectGroupPermissions(groupId).Contains(ProjectGroupPermission.ViewProjectGroup);

    /// <summary>ManageProjectGroups, or a manager of the group: a member whose role has AllPermissions or ManageProject.</summary>
    public bool CanManage(Guid groupId) =>
        CanManageGroups() || authorizationService.GetProjectGroupPermissions(groupId).Contains(ProjectGroupPermission.ManageMembership);

    public bool CanListMemberships(Guid groupId) => Has(SystemPermission.ViewProjects) || CanManage(groupId);

    /// <summary>ManageProjects, or ManageProject on the project itself.</summary>
    public bool CanManageProject(Guid projectId) =>
        Has(SystemPermission.ManageProjects) ||
        authorizationService.GetProjectPermissions(projectId).Any(x => x.Permissions.Contains(ProjectPermission.ManageProject));

    public System.Collections.Generic.IEnumerable<Guid> MemberGroupIds() => authorizationService.GetProjectGroupIds();
}

public static class ProjectGroupQueries
{
    public static async Task<ProjectGroup> GetView(this CasterContext dbContext, Guid id, ProjectGroupAccess access, CancellationToken cancellationToken) =>
        (await dbContext.GetViews(x => x.Id == id, access, cancellationToken)).FirstOrDefault()
        ?? throw new EntityNotFoundException<ProjectGroup>();

    public static async Task<ProjectGroup[]> GetViews(
        this CasterContext dbContext,
        System.Linq.Expressions.Expression<Func<Domain.Models.ProjectGroup, bool>> filter,
        ProjectGroupAccess access,
        CancellationToken cancellationToken)
    {
        var groups = await dbContext.ProjectGroups
            .AsNoTracking()
            .Where(filter)
            .OrderBy(x => x.Name)
            .Select(x => new ProjectGroup
            {
                Id = x.Id,
                Name = x.Name,
                Description = x.Description,
                PartitionId = x.PartitionId,
                Projects = x.Projects.OrderBy(p => p.Name).Select(p => new ProjectGroupItem { Id = p.Id, Name = p.Name }).ToArray(),
                EnvironmentSets = x.EnvironmentSets.OrderBy(s => s.EnvironmentSet.Name)
                    .Select(s => new ProjectGroupItem { Id = s.EnvironmentSetId, Name = s.EnvironmentSet.Name }).ToArray(),
                InventoryConnections = x.InventoryConnections.OrderBy(c => c.ConnectionName).Select(c => c.ConnectionName).ToArray(),
                Hosts = x.Hosts.OrderBy(h => h.Name).Select(h => new ProjectGroupItem { Id = h.Id, Name = h.Name }).ToArray()
            })
            .ToArrayAsync(cancellationToken);

        foreach (var group in groups)
        {
            group.CanManage = access.CanManage(group.Id);
        }

        return groups;
    }

    public static Task<ProjectGroupMembership[]> GetMembershipViews(this CasterContext dbContext, Guid groupId, CancellationToken cancellationToken) =>
        dbContext.ProjectGroupMemberships
            .AsNoTracking()
            .Where(x => x.ProjectGroupId == groupId)
            .Select(x => new ProjectGroupMembership
            {
                Id = x.Id,
                ProjectGroupId = x.ProjectGroupId,
                UserId = x.UserId,
                UserName = x.User == null ? null : x.User.Name,
                GroupId = x.GroupId,
                GroupName = x.Group == null ? null : x.Group.Name,
                RoleId = x.RoleId,
                RoleName = x.Role.Name
            })
            .OrderBy(x => x.UserName ?? x.GroupName)
            .ToArrayAsync(cancellationToken);
}
