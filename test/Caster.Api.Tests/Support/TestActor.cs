// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// Mirrors UserClaimsService.GetPermissionClaims: system permissions from User.RoleId -> SystemRole
// (AllPermissions or Permissions); project permissions from every ProjectMembership of the user, or of a
// group the user belongs to, through its ProjectRole; and GroupPermission.ManageMembership on every group
// the user is a Manager of.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;

namespace Caster.Api.Tests.Support;

/// <summary>A seeded user, and the ids of the rows seeded with them.</summary>
/// <remarks>
/// An HTTP test acts as an actor rather than as a hand-built principal: <c>ApiTestBase.Client(actor)</c>
/// puts the id on the request and the real <c>AuthorizationClaimsTransformer</c> derives the permission
/// claims from these rows. What the actor may do is therefore a property of the database, as in production.
/// </remarks>
public sealed class TestActor
{
    public required Guid Id { get; init; }

    /// <summary>Sent as the <c>name</c> claim, which <c>UserClaimsService.ValidateUser</c> writes back to the user row.</summary>
    public required string Name { get; init; }

    /// <summary>One per <c>OnProject</c>, <c>OnNewProject</c> and <c>OnProjectThroughNewGroup</c> call, in order.</summary>
    public required IReadOnlyList<TestActorProjectMembership> ProjectMemberships { get; init; }

    /// <summary>One per <c>InGroup</c> and <c>OnNewGroup</c> call, in order.</summary>
    public required IReadOnlyList<TestActorGroupMembership> GroupMemberships { get; init; }

    /// <summary>The first project membership: the common case of an actor on one project.</summary>
    public TestActorProjectMembership ProjectMembership => ProjectMemberships.Count > 0
        ? ProjectMemberships[0]
        : throw new InvalidOperationException($"Actor {Id} is on no project.");

    /// <summary>The first group membership.</summary>
    public TestActorGroupMembership GroupMembership => GroupMemberships.Count > 0
        ? GroupMemberships[0]
        : throw new InvalidOperationException($"Actor {Id} is in no group.");
}

/// <summary>
/// One seeded project membership. <see cref="GroupId"/> is set when the membership belongs to a group the
/// actor is in rather than to the actor.
/// </summary>
public sealed record TestActorProjectMembership(Guid ProjectId, Guid MembershipId, Guid RoleId, Guid? GroupId);

/// <summary>One seeded group membership.</summary>
public sealed record TestActorGroupMembership(Guid GroupId, Guid MembershipId, GroupMembershipRole Role);

/// <summary>
/// Seeds a user, the role that grants their system permissions, and their project and group memberships, so
/// that the real claims transformer derives exactly the permissions a test names.
/// </summary>
/// <remarks>
/// <para>
/// Roles are minted per actor rather than shared: <c>system_roles.name</c> is uniquely indexed, and a
/// shared role would let one test's grant answer another's. Where a seeded role says what a test means
/// (<c>TestData.Roles.Administrator</c>, <c>TestData.ProjectRoles.Manager</c>), pass its id instead.
/// </para>
/// <para>
/// A project membership always has a role (<c>ProjectMembership.RoleId</c> defaults to the seeded Member
/// role, which grants ViewProject, EditProject and ImportProject), so <c>OnProject</c> refuses a call that
/// names neither permissions nor a role rather than inheriting that default. <c>OnNewProject</c> is the near
/// miss "the right permission on another project": it mints a project of its own.
/// </para>
/// </remarks>
public sealed class TestActorBuilder(CasterContext db, CancellationToken ct)
{
    private readonly List<PendingProjectMembership> _projects = [];
    private readonly List<PendingGroupMembership> _groups = [];
    private Guid _id = Guid.NewGuid();
    private string _name = "Test Actor";
    private Guid? _roleId;
    private SystemPermission[] _systemPermissions;

    /// <summary>Fixes the actor's id, for a test that needs to know it before seeding.</summary>
    public TestActorBuilder WithId(Guid id)
    {
        _id = id;
        return this;
    }

    public TestActorBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>Gives the actor an existing system role, such as <c>TestData.Roles.Observer</c>.</summary>
    public TestActorBuilder WithRole(Guid roleId)
    {
        if (_systemPermissions is not null)
        {
            throw new InvalidOperationException(
                "WithRole and WithSystemPermissions both decide the actor's system role. Drop one.");
        }

        _roleId = roleId;
        return this;
    }

    /// <summary>Every system permission, by way of the seeded Administrator role (<c>AllPermissions</c>).</summary>
    public TestActorBuilder WithAllSystemPermissions() => WithRole(TestData.Roles.Administrator);

    /// <summary>Exactly these system permissions, by way of a role minted for this actor.</summary>
    public TestActorBuilder WithSystemPermissions(params SystemPermission[] permissions)
    {
        if (_roleId is not null)
        {
            throw new InvalidOperationException(
                "WithSystemPermissions and WithRole both decide the actor's system role. Drop one.");
        }

        _systemPermissions = permissions;
        return this;
    }

    /// <summary>
    /// Puts the actor on <paramref name="project"/>, which must already be saved. Passing
    /// <paramref name="permissions"/> mints a project role for this membership alone;
    /// <paramref name="roleId"/> names an existing one instead. One of the two is required.
    /// </summary>
    public TestActorBuilder OnProject(Project project, ProjectPermission[] permissions = null, Guid? roleId = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        _projects.Add(Pending(project.Id, permissions, roleId, throughGroup: false));
        return this;
    }

    /// <summary>
    /// Puts the actor on a new project of its own, minted in <see cref="SeedAsync"/>, holding exactly
    /// <paramref name="permissions"/> there: the near miss of a denied test that names the right permission
    /// on the wrong project. The minted id is on <see cref="TestActor.ProjectMemberships"/>.
    /// </summary>
    public TestActorBuilder OnNewProject(params ProjectPermission[] permissions)
    {
        _projects.Add(Pending(null, permissions ?? [], null, throughGroup: false));
        return this;
    }

    /// <summary>
    /// Puts the actor in a new group, and that group on <paramref name="project"/> with exactly
    /// <paramref name="permissions"/>: the path by which a group grants its members project permissions.
    /// </summary>
    public TestActorBuilder OnProjectThroughNewGroup(Project project, params ProjectPermission[] permissions)
    {
        ArgumentNullException.ThrowIfNull(project);
        _projects.Add(Pending(project.Id, permissions ?? [], null, throughGroup: true));
        return this;
    }

    /// <summary>
    /// Puts the actor in <paramref name="group"/>, which must already be saved. A Manager holds
    /// <c>GroupPermission.ManageMembership</c> on it; a Member holds nothing.
    /// </summary>
    public TestActorBuilder InGroup(Group group, GroupMembershipRole role = GroupMembershipRole.Member)
    {
        ArgumentNullException.ThrowIfNull(group);
        _groups.Add(new PendingGroupMembership(group.Id, role));
        return this;
    }

    /// <summary>
    /// Puts the actor in a new group of its own, minted in <see cref="SeedAsync"/>: the near miss "manages
    /// another group". The minted id is on <see cref="TestActor.GroupMemberships"/>.
    /// </summary>
    public TestActorBuilder OnNewGroup(GroupMembershipRole role = GroupMembershipRole.Manager)
    {
        _groups.Add(new PendingGroupMembership(null, role));
        return this;
    }

    /// <summary>Writes the actor and everything above to the database.</summary>
    public async Task<TestActor> SeedAsync()
    {
        var roleId = _roleId;

        if (_systemPermissions is not null)
        {
            var role = TestData.SystemRole(permissions: _systemPermissions);
            db.SystemRoles.Add(role);
            roleId = role.Id;
        }

        db.Users.Add(TestData.User(_id, _name, roleId));

        List<TestActorProjectMembership> projects = [];

        foreach (var pending in _projects)
        {
            var projectId = pending.ProjectId ?? MintProject();
            var projectRoleId = pending.RoleId ?? MintProjectRole(pending.Permissions);
            Guid? groupId = null;

            if (pending.ThroughGroup)
            {
                var group = TestData.Group();
                db.Groups.Add(group);
                db.GroupMemberships.Add(TestData.GroupMembership(group.Id, _id));
                groupId = group.Id;
            }

            var membership = TestData.ProjectMembership(
                projectId, projectRoleId, userId: groupId is null ? _id : null, groupId: groupId);
            db.ProjectMemberships.Add(membership);

            projects.Add(new TestActorProjectMembership(projectId, membership.Id, projectRoleId, groupId));
        }

        List<TestActorGroupMembership> groups = [];

        foreach (var pending in _groups)
        {
            var groupId = pending.GroupId ?? MintGroup();
            var membership = TestData.GroupMembership(groupId, _id, pending.Role);
            db.GroupMemberships.Add(membership);

            groups.Add(new TestActorGroupMembership(groupId, membership.Id, pending.Role));
        }

        await db.SaveChangesAsync(ct);

        return new TestActor
        {
            Id = _id,
            Name = _name,
            ProjectMemberships = projects,
            GroupMemberships = groups
        };
    }

    private static PendingProjectMembership Pending(
        Guid? projectId, ProjectPermission[] permissions, Guid? roleId, bool throughGroup)
    {
        if (permissions is not null && roleId is not null)
        {
            throw new InvalidOperationException(
                "A project membership names a role and also asks for permissions, which would mint a second " +
                "role. Pass one or the other.");
        }

        if (permissions is null && roleId is null)
        {
            throw new InvalidOperationException(
                "A project membership always has a role, and the default (Member) grants ViewProject, " +
                "EditProject and ImportProject. Name the permissions or the role.");
        }

        return new PendingProjectMembership(projectId, permissions, roleId, throughGroup);
    }

    private Guid MintProject()
    {
        var project = TestData.Project("Another Project");
        db.Projects.Add(project);

        return project.Id;
    }

    private Guid MintProjectRole(ProjectPermission[] permissions)
    {
        var role = TestData.ProjectRole(permissions: permissions);
        db.ProjectRoles.Add(role);

        return role.Id;
    }

    private Guid MintGroup()
    {
        var group = TestData.Group();
        db.Groups.Add(group);

        return group.Id;
    }

    private sealed record PendingProjectMembership(
        Guid? ProjectId, ProjectPermission[] Permissions, Guid? RoleId, bool ThroughGroup);

    private sealed record PendingGroupMembership(Guid? GroupId, GroupMembershipRole Role);
}
