// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// The seeded ids are Caster's HasData rows (SystemRoleConfiguration, ProjectRoleConfiguration), which the
// migrations insert into the template database. appsettings.json SeedData runs only in InitializeDatabase,
// against the host's own database, and is empty as shipped.

using System;
using Caster.Api.Domain.Models;
using Directory = Caster.Api.Domain.Models.Directory;
using File = Caster.Api.Domain.Models.File;

namespace Caster.Api.Tests.Support;

/// <summary>Object mothers, and the ids of the rows the migrations seed.</summary>
/// <remarks>
/// Every mother sets an explicit id, so a test can compose references before saving, and fills only what
/// the schema requires; a test sets what it asserts on. Children take their parent entity rather than its id
/// where Caster's model only exposes the foreign key through the navigation (<see cref="File"/>).
/// </remarks>
public static class TestData
{
    /// <summary>Ids of the system roles the migrations seed.</summary>
    public static class Roles
    {
        /// <summary><c>AllPermissions</c>: every <see cref="SystemPermission"/>.</summary>
        public static readonly Guid Administrator = SystemRoleDefaults.AdministratorRoleId;

        /// <summary><c>CreateProjects</c> only.</summary>
        public static readonly Guid ContentDeveloper = SystemRoleDefaults.ContentDeveloperRoleId;

        /// <summary>Every permission whose name starts with <c>View</c>.</summary>
        public static readonly Guid Observer = SystemRoleDefaults.ObserverRoleId;
    }

    /// <summary>Ids of the project roles the migrations seed.</summary>
    public static class ProjectRoles
    {
        /// <summary>Named "Manager": <c>AllPermissions</c>.</summary>
        public static readonly Guid Manager = ProjectRoleDefaults.ProjectCreatorRoleId;

        /// <summary>Named "Member": ViewProject, EditProject, ImportProject. The default of a new membership.</summary>
        public static readonly Guid Member = ProjectRoleDefaults.ProjectMemberRoleId;

        /// <summary>Named "Observer": ViewProject.</summary>
        public static readonly Guid Observer = ProjectRoleDefaults.ProjectReadOnlyRoleId;
    }

    public static User User(Guid? id = null, string name = "Test User", Guid? roleId = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            RoleId = roleId
        };

    /// <summary>A system role of its own, named uniquely because <c>system_roles.name</c> is uniquely indexed.</summary>
    public static SystemRole SystemRole(bool allPermissions = false, SystemPermission[] permissions = null, string name = null)
    {
        var id = Guid.NewGuid();

        return new SystemRole
        {
            Id = id,
            Name = name ?? $"role-{id:N}",
            AllPermissions = allPermissions,
            Permissions = [.. permissions ?? []]
        };
    }

    /// <summary>A project role of its own. Its name is not indexed, so it only has to be readable.</summary>
    public static ProjectRole ProjectRole(bool allPermissions = false, ProjectPermission[] permissions = null)
    {
        var id = Guid.NewGuid();

        return new ProjectRole
        {
            Id = id,
            Name = $"project-role-{id:N}",
            AllPermissions = allPermissions,
            Permissions = [.. permissions ?? []]
        };
    }

    public static Project Project(string name = "Test Project") =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            DateCreated = DefaultDateCreated
        };

    /// <summary>A membership of <paramref name="userId"/> or <paramref name="groupId"/> on a project, with an explicit role.</summary>
    public static ProjectMembership ProjectMembership(Guid projectId, Guid roleId, Guid? userId = null, Guid? groupId = null) =>
        new(projectId, userId, groupId)
        {
            Id = Guid.NewGuid(),
            RoleId = roleId
        };

    /// <summary>A group, named uniquely by default because <c>groups.name</c> is uniquely indexed.</summary>
    public static Group Group(string name = null)
    {
        var id = Guid.NewGuid();

        return new Group
        {
            Id = id,
            Name = name ?? $"group-{id:N}"
        };
    }

    public static GroupMembership GroupMembership(Guid groupId, Guid userId, GroupMembershipRole role = GroupMembershipRole.Member) =>
        new(groupId, userId, role) { Id = Guid.NewGuid() };

    /// <summary>A top-level directory of <paramref name="project"/>, or a child of <paramref name="parent"/>.</summary>
    public static Directory Directory(Project project, string name = "test-directory", Directory parent = null)
    {
        var directory = new Directory
        {
            Id = Guid.NewGuid(),
            Name = name,
            ProjectId = project.Id
        };

        if (parent is null)
        {
            directory.SetPath();
        }
        else
        {
            directory.ParentId = parent.Id;
            directory.SetPath(parent.Path);
        }

        return directory;
    }

    /// <summary>A file in <paramref name="directory"/>, which must be the tracked instance (the key is private).</summary>
    public static File File(Directory directory, string name = "main.tf", string content = "# test") =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Content = content,
            Directory = directory
        };

    /// <summary>One saved version of <paramref name="file"/>, with its content as it is now.</summary>
    public static FileVersion FileVersion(File file) =>
        new(file)
        {
            Id = Guid.NewGuid(),
            DateSaved = DefaultDateCreated
        };

    public static Workspace Workspace(Directory directory, string name = "test-workspace") =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            DirectoryId = directory.Id
        };

    /// <summary>A run of <paramref name="workspace"/>, queued unless a test says otherwise.</summary>
    public static Run Run(Workspace workspace, RunStatus status = RunStatus.Queued) =>
        new()
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            Status = status,
            CreatedAt = DefaultDateCreated,
            ModifiedAt = DefaultDateCreated
        };

    public static Plan Plan(Run run, PlanStatus status = PlanStatus.Planned, string output = "plan output") =>
        new()
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            Status = status,
            Output = output
        };

    public static Apply Apply(Run run, ApplyStatus status = ApplyStatus.Applied, string output = "apply output") =>
        new()
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            Status = status,
            Output = output
        };

    public static Design Design(Directory directory, string name = "test-design") =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            DirectoryId = directory.Id
        };

    public static Module Module(string name = "test-module") =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = $"modules/{name}",
            DateModified = DefaultDateCreated
        };

    public static ModuleVersion ModuleVersion(Module module, string name = "1.0.0") =>
        new()
        {
            Id = Guid.NewGuid(),
            ModuleId = module.Id,
            Name = name,
            UrlLink = $"https://gitlab.test/{module.Path}?ref={name}",
            DateCreated = DefaultDateCreated
        };

    /// <summary>A module of <paramref name="design"/>, with no values yet.</summary>
    public static DesignModule DesignModule(Design design, Module module, string name = "test-design-module") =>
        new()
        {
            Id = Guid.NewGuid(),
            DesignId = design.Id,
            ModuleId = module.Id,
            Name = name,
            ModuleVersion = "1.0.0",
            // DesignModules/Create always stores a list (an empty one when none is sent).
            Values = []
        };

    public static Variable Variable(Design design, string name = "test_variable") =>
        new()
        {
            Id = Guid.NewGuid(),
            DesignId = design.Id,
            Name = name,
            Type = VariableType.@string,
            DefaultValue = "value"
        };

    public static Host Host(string name = "test-host") =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Datastore = "datastore1",
            MaximumMachines = 10,
            Enabled = true
        };

    public static Pool Pool(string name = "test-pool") =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name
        };

    public static Partition Partition(Pool pool, string name = "test-partition") =>
        new()
        {
            Id = Guid.NewGuid(),
            PoolId = pool.Id,
            Name = name
        };

    /// <summary>One VLAN of <paramref name="pool"/>, in <paramref name="partition"/> when given.</summary>
    public static Vlan Vlan(Pool pool, int vlanId, Partition partition = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            PoolId = pool.Id,
            PartitionId = partition?.Id,
            VlanId = vlanId
        };

    /// <summary>A fixed creation timestamp. Tests that care about ordering pass their own.</summary>
    public static readonly DateTime DefaultDateCreated = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}
