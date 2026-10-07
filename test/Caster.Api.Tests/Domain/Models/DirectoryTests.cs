// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;

namespace Caster.Api.Tests.Domain.Models;

/// <summary>Pure tests of <see cref="Directory"/>'s path and import-name helpers.</summary>
public class DirectoryTests
{
    [Fact]
    public void PathIds_lists_the_ancestors_then_the_directory_itself()
    {
        var greatGrandparentId = Guid.NewGuid();
        var grandparentId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var directory = new Directory { Id = id, ProjectId = Guid.NewGuid(), ParentId = parentId };

        directory.SetPath($"{greatGrandparentId}/{grandparentId}/{parentId}/");

        Assert.Equal([greatGrandparentId, grandparentId, parentId, id], directory.PathIds());
    }

    [Fact]
    public void SetImportName_takes_the_id_from_a_name_ending_in_a_guid()
    {
        var directory = new Directory();

        directory.SetImportName("DirectoryWithId__b7ef25e6-555e-41c9-88d7-22078d3a13c1");

        Assert.Equal("DirectoryWithId", directory.Name);
        Assert.Equal(new Guid("b7ef25e6-555e-41c9-88d7-22078d3a13c1"), directory.Id);
    }

    [Fact]
    public void SetImportName_keeps_a_plain_name_and_no_id()
    {
        var directory = new Directory();

        directory.SetImportName("Directory");

        Assert.Equal("Directory", directory.Name);
        Assert.Equal(Guid.Empty, directory.Id);
    }

    [Fact]
    public void SetImportName_keeps_the_whole_name_when_the_suffix_is_not_a_guid()
    {
        var directory = new Directory();

        directory.SetImportName("DirectoryWithInvalidId__b7ef25e6-555e-41c9-88d7822078d3a13c1");

        Assert.Equal("DirectoryWithInvalidId__b7ef25e6-555e-41c9-88d7822078d3a13c1", directory.Name);
        Assert.Equal(Guid.Empty, directory.Id);
    }
}
