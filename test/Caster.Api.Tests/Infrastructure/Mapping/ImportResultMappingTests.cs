// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
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
}
