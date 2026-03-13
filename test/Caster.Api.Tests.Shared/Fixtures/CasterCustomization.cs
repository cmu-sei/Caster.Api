// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoFixture;
using Caster.Api.Domain.Models;
using Directory = Caster.Api.Domain.Models.Directory;
using File = Caster.Api.Domain.Models.File;

namespace Caster.Api.Tests.Shared.Fixtures;

/// <summary>
/// AutoFixture customization that registers entity factory rules for all Caster domain entities.
/// Prevents circular reference issues and ensures entities are created with valid default values.
/// </summary>
public class CasterCustomization : ICustomization
{
    public void Customize(IFixture fixture)
    {
        fixture.Behaviors.OfType<ThrowingRecursionBehavior>()
            .ToList()
            .ForEach(b => fixture.Behaviors.Remove(b));
        fixture.Behaviors.Add(new OmitOnRecursionBehavior());

        // Project
        fixture.Customize<Project>(c => c
            .Without(x => x.Directories)
            .Without(x => x.Memberships)
            .Without(x => x.Partition));

        // Directory
        fixture.Customize<Directory>(c => c
            .Without(x => x.Project)
            .Without(x => x.Parent)
            .Without(x => x.Files)
            .Without(x => x.Workspaces)
            .Without(x => x.Children)
            .Without(x => x.Designs)
            .Do(d => d.SetPath()));

        // File
        fixture.Customize<File>(c => c
            .Without(x => x.Directory)
            .Without(x => x.Workspace)
            .Without(x => x.FileVersions));

        // FileVersion
        fixture.Customize<FileVersion>(c => c
            .Without(x => x.File)
            .Without(x => x.ModifiedBy)
            .Without(x => x.TaggedBy));

        // Workspace
        fixture.Customize<Workspace>(c => c
            .Without(x => x.Directory)
            .Without(x => x.Host)
            .Without(x => x.Runs)
            .Without(x => x.Files)
            .With(x => x.SyncErrors, Array.Empty<string>()));

        // Run
        fixture.Customize<Run>(c => c
            .Without(x => x.Plan)
            .Without(x => x.Apply)
            .Without(x => x.Workspace)
            .Without(x => x.CreatedBy)
            .Without(x => x.ModifiedBy)
            .With(x => x.Targets, Array.Empty<string>())
            .With(x => x.ReplaceAddresses, Array.Empty<string>()));

        // Plan
        fixture.Customize<Plan>(c => c
            .Without(x => x.Run));

        // Apply
        fixture.Customize<Apply>(c => c
            .Without(x => x.Run));

        // User
        fixture.Customize<User>(c => c
            .Without(x => x.Role)
            .Without(x => x.ProjectMemberships)
            .Without(x => x.GroupMemberships));

        // Host
        fixture.Customize<Host>(c => c
            .Without(x => x.Project)
            .Without(x => x.Machines));

        // HostMachine
        fixture.Customize<HostMachine>(c => c
            .Without(x => x.Workspace)
            .Without(x => x.Host));

        // Module
        fixture.Customize<Module>(c => c
            .Without(x => x.Versions));

        // ModuleVersion
        fixture.Customize<ModuleVersion>(c => c
            .Without(x => x.Module)
            .With(x => x.Variables, new List<ModuleVariable>())
            .With(x => x.Outputs, new List<ModuleOutput>()));

        // Design
        fixture.Customize<Design>(c => c
            .Without(x => x.Directory)
            .Without(x => x.Modules)
            .Without(x => x.Variables));

        // DesignModule
        fixture.Customize<DesignModule>(c => c
            .Without(x => x.Design)
            .Without(x => x.Module));

        // Variable
        fixture.Customize<Variable>(c => c
            .Without(x => x.Design));

        // Vlan
        fixture.Customize<Vlan>(c => c
            .With(x => x.ReservedEditable, true));

        // Pool
        fixture.Customize<Pool>(c => c
            .Without(x => x.Partitions)
            .Without(x => x.Vlans));

        // Partition
        fixture.Customize<Partition>(c => c
            .Without(x => x.Pool)
            .Without(x => x.Vlans));

        // SystemRole
        fixture.Customize<SystemRole>(c => c
            .With(x => x.Permissions, new List<SystemPermission>()));

        // ProjectRole
        fixture.Customize<ProjectRole>(c => c
            .With(x => x.Permissions, new List<ProjectPermission>()));

        // ProjectMembership
        fixture.Customize<ProjectMembership>(c => c
            .Without(x => x.Project)
            .Without(x => x.User)
            .Without(x => x.Group)
            .Without(x => x.Role));

        // Group
        fixture.Customize<Group>(c => c
            .Without(x => x.Memberships)
            .Without(x => x.ProjectMemberships));

        // GroupMembership
        fixture.Customize<GroupMembership>(c => c
            .Without(x => x.Group)
            .Without(x => x.User));

        // ContainerImage
        fixture.Customize<ContainerImage>(c => c
            .With(x => x.Tags, new[] { "latest" }));

        // RemovedResource - has string Id
        fixture.Customize<RemovedResource>(c => c
            .With(x => x.Id, Guid.NewGuid().ToString()));
    }
}
