// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Directory = Caster.Api.Domain.Models.Directory;
using File = Caster.Api.Domain.Models.File;

namespace Caster.Api.Tests.Support;

/// <summary>
/// Two projects with the same shape, for the tests of what a body names against what the route authorizes:
/// a caller allowed on <see cref="ProjectA"/> names rows of <see cref="ProjectB"/> (or of another part of A)
/// in the body. Seeds no actors.
/// </summary>
/// <remarks>
/// <para>Project A: <see cref="DirectoryA"/> with <see cref="ChildA"/> and its <see cref="GrandchildA"/>, and the
/// top-level <see cref="AlternateA"/>; <see cref="WorkspaceA"/> in DirectoryA and <see cref="AlternateWorkspaceA"/>
/// in AlternateA; <see cref="FileA"/> (in WorkspaceA), <see cref="AnotherFileA"/> and the deleted
/// <see cref="DeletedFileA"/> in DirectoryA; <see cref="DesignA"/> with <see cref="DesignModuleA"/>.</para>
/// <para>Project B: <see cref="DirectoryB"/>, <see cref="WorkspaceB"/>, <see cref="FileB"/> (in WorkspaceB), the
/// deleted <see cref="DeletedFileB"/>, <see cref="DesignB"/> with <see cref="DesignModuleB"/>.</para>
/// <para>Every file is saved once (one version each, its content its name); FileB and AnotherFileA are also
/// tagged <see cref="SharedTag"/>, so the graph holds <see cref="VersionCount"/> versions. No file is locked.
/// Both design modules use <see cref="Module"/>, which has the versions v1 and v2.</para>
/// </remarks>
public sealed class TwoProjectGraph
{
    /// <summary>The files the graph holds, deleted ones included.</summary>
    public const int FileCount = 5;

    /// <summary>One saved version per file, and one tagged version each for FileB and AnotherFileA.</summary>
    public const int VersionCount = 7;

    /// <summary>The tag FileB (project B) and AnotherFileA (project A) already carry.</summary>
    public const string SharedTag = "shared";

    /// <summary>Every workspace's state, which no edit may lose.</summary>
    public const string State = "sensitive-state";

    /// <summary>Every workspace's state backup, which no edit may lose.</summary>
    public const string StateBackup = "sensitive-backup";

    public Project ProjectA { get; private init; }
    public Project ProjectB { get; private init; }
    public Directory DirectoryA { get; private init; }
    public Directory DirectoryB { get; private init; }
    public Directory ChildA { get; private init; }
    public Directory GrandchildA { get; private init; }
    public Directory AlternateA { get; private init; }
    public Workspace WorkspaceA { get; private init; }
    public Workspace WorkspaceB { get; private init; }
    public Workspace AlternateWorkspaceA { get; private init; }
    public File FileA { get; private init; }
    public File FileB { get; private init; }
    public File AnotherFileA { get; private init; }
    public File DeletedFileA { get; private init; }
    public File DeletedFileB { get; private init; }
    public Design DesignA { get; private init; }
    public Design DesignB { get; private init; }
    public Module Module { get; private init; }
    public DesignModule DesignModuleA { get; private init; }
    public DesignModule DesignModuleB { get; private init; }

    /// <summary>
    /// Saves the graph to <paramref name="db"/>. <paramref name="savedBy"/>, an existing user, is recorded as the
    /// user who saved and tagged every file.
    /// </summary>
    public static async Task<TwoProjectGraph> SeedAsync(CasterContext db, Guid savedBy, CancellationToken ct)
    {
        var projectA = TestData.Project("A");
        var projectB = TestData.Project("B");
        var directoryA = TestData.Directory(projectA, "A");
        var directoryB = TestData.Directory(projectB, "B");
        var childA = TestData.Directory(projectA, "child", directoryA);
        var grandchildA = TestData.Directory(projectA, "grandchild", childA);
        var alternateA = TestData.Directory(projectA, "alternate");
        var workspaceA = Workspace(directoryA, "workspace-a");
        var workspaceB = Workspace(directoryB, "workspace-b");
        var alternateWorkspaceA = Workspace(alternateA, "alternate");
        var module = TestData.Module("catalog");
        var designA = Design(directoryA, "design-a");
        var designB = Design(directoryB, "design-b");

        var graph = new TwoProjectGraph
        {
            ProjectA = projectA,
            ProjectB = projectB,
            DirectoryA = directoryA,
            DirectoryB = directoryB,
            ChildA = childA,
            GrandchildA = grandchildA,
            AlternateA = alternateA,
            WorkspaceA = workspaceA,
            WorkspaceB = workspaceB,
            AlternateWorkspaceA = alternateWorkspaceA,
            FileA = SavedFile("a.tf", directoryA, savedBy, workspaceA),
            FileB = SavedFile("b.tf", directoryB, savedBy, workspaceB),
            AnotherFileA = SavedFile("another.tf", directoryA, savedBy),
            DeletedFileA = SavedFile("deleted-a.tf", directoryA, savedBy, deleted: true),
            DeletedFileB = SavedFile("deleted-b.tf", directoryB, savedBy, deleted: true),
            DesignA = designA,
            DesignB = designB,
            Module = module,
            DesignModuleA = DesignModule(designA, module, "before"),
            DesignModuleB = DesignModule(designB, module, "victim"),
        };

        graph.FileB.Tag(SharedTag, savedBy, DateTime.UtcNow);
        graph.AnotherFileA.Tag(SharedTag, savedBy, DateTime.UtcNow);

        db.AddRange(
            projectA, projectB,
            directoryA, directoryB, childA, grandchildA, alternateA,
            workspaceA, workspaceB, alternateWorkspaceA,
            graph.FileA, graph.FileB, graph.AnotherFileA, graph.DeletedFileA, graph.DeletedFileB,
            designA, designB, module, graph.DesignModuleA, graph.DesignModuleB,
            TestData.ModuleVersion(module, "v1"), TestData.ModuleVersion(module, "v2"));
        await db.SaveChangesAsync(ct);

        return graph;
    }

    private static Workspace Workspace(Directory directory, string name)
    {
        var workspace = TestData.Workspace(directory, name);
        workspace.State = State;
        workspace.StateBackup = StateBackup;

        return workspace;
    }

    /// <summary>Disabled, so no design module is written into a workspace's files.</summary>
    private static Design Design(Directory directory, string name)
    {
        var design = TestData.Design(directory, name);
        design.Enabled = false;

        return design;
    }

    /// <summary>A module at version v1 whose one value, <c>input</c>, is <paramref name="name"/>.</summary>
    private static DesignModule DesignModule(Design design, Module module, string name)
    {
        var designModule = TestData.DesignModule(design, module, name);
        designModule.ModuleVersion = "v1";
        designModule.Values = [new ModuleValue { Name = "input", Value = name }];

        return designModule;
    }

    /// <summary>A file whose content is its name, saved once by <paramref name="savedBy"/>.</summary>
    private static File SavedFile(string name, Directory directory, Guid savedBy, Workspace workspace = null, bool deleted = false)
    {
        var file = TestData.File(directory, name, content: name);
        file.Workspace = workspace;
        file.Save(savedBy, canLock: true, bypassLock: true);
        file.IsDeleted = deleted;

        return file;
    }
}
