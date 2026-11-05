// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
using Directory = Caster.Api.Domain.Models.Directory;
using File = Caster.Api.Domain.Models.File;
using ImportDirectoryResult = Caster.Api.Features.Directories.Import.ImportDirectoryResult;
using ImportProjectResult = Caster.Api.Features.Projects.Import.ImportProjectResult;

namespace Caster.Api.Tests.Infrastructure.Mapping;

/// <summary>The maps from the import service's <see cref="ImportResult"/> onto the two import endpoints' responses.</summary>
public class ImportResultMappingTests
{
    [Fact]
    public void Skipped_Settings_Are_Surfaced_In_The_Import_Responses()
    {
        var importResult = new ImportResult
        {
            LockedFiles = [],
            SkippedSettings = ["Workspace 'a': skipped"],
            Warnings = ["exported from somewhere else"]
        };

        var projectResult = TestMapper.Mapper.Map<ImportProjectResult>(importResult);
        var directoryResult = TestMapper.Mapper.Map<ImportDirectoryResult>(importResult);

        Assert.Equal(importResult.SkippedSettings, projectResult.SkippedSettings);
        Assert.Equal(importResult.Warnings, projectResult.Warnings);
        Assert.Equal(importResult.SkippedSettings, directoryResult.SkippedSettings);
        Assert.Equal(importResult.Warnings, directoryResult.Warnings);
    }

    /// <summary>The locked files are answered by path.</summary>
    [Fact]
    public void Locked_Files_Are_Answered_By_Path()
    {
        var project = new Project { Id = Guid.NewGuid(), Name = "p" };
        var directory = new Directory("dir");
        directory.ProjectId = project.Id;
        var importResult = new ImportResult { LockedFiles = [new File { Name = "main.tf", Directory = directory }] };

        var projectResult = TestMapper.Mapper.Map<ImportProjectResult>(importResult);
        var directoryResult = TestMapper.Mapper.Map<ImportDirectoryResult>(importResult);

        Assert.Equal(["dir/main.tf"], projectResult.LockedFiles);
        Assert.Equal(["dir/main.tf"], directoryResult.LockedFiles);
    }

    /// <summary>A result without locked files, skipped settings or warnings is answered with empty lists, not nulls.</summary>
    [Fact]
    public void Missing_Lists_Are_Answered_As_Empty_Lists()
    {
        var importResult = new ImportResult { LockedFiles = null, SkippedSettings = null, Warnings = null };

        var projectResult = TestMapper.Mapper.Map<ImportProjectResult>(importResult);
        var directoryResult = TestMapper.Mapper.Map<ImportDirectoryResult>(importResult);

        foreach (var lists in new[] { (projectResult.LockedFiles, projectResult.SkippedSettings, projectResult.Warnings),
                     (directoryResult.LockedFiles, directoryResult.SkippedSettings, directoryResult.Warnings) })
        {
            Assert.NotNull(lists.LockedFiles);
            Assert.Empty(lists.LockedFiles);
            Assert.NotNull(lists.SkippedSettings);
            Assert.Empty(lists.SkippedSettings);
            Assert.NotNull(lists.Warnings);
            Assert.Empty(lists.Warnings);
        }
    }
}
