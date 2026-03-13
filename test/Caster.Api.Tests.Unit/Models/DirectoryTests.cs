// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;
using Xunit;

namespace Caster.Api.Tests.Unit.Models
{
    [Trait("Category", "Unit")]
    public class DirectoryTests
    {
        [Fact]
        public void PathIds_WithNestedHierarchy_ReturnsAllIds()
        {
            var greatGrandparentId = Guid.NewGuid();
            var grandparentId = Guid.NewGuid();
            var parentId = Guid.NewGuid();
            var id = Guid.NewGuid();

            var directory = new Directory()
            {
                Id = id,
                ProjectId = Guid.NewGuid(),
                ParentId = parentId
            };

            directory.SetPath($"{greatGrandparentId}/{grandparentId}/{parentId}/");

            var expectedPathIds = new Guid[] { greatGrandparentId, grandparentId, parentId, id };
            var pathIds = directory.PathIds();

            Assert.Equal(expectedPathIds, pathIds);
        }

        [Theory]
        [InlineData("DirectoryWithId__b7ef25e6-555e-41c9-88d7-22078d3a13c1", "DirectoryWithId", "b7ef25e6-555e-41c9-88d7-22078d3a13c1")]
        [InlineData("Directory", "Directory", "00000000-0000-0000-0000-000000000000")]
        [InlineData("DirectoryWithInvalidId__b7ef25e6-555e-41c9-88d7822078d3a13c1", "DirectoryWithInvalidId__b7ef25e6-555e-41c9-88d7822078d3a13c1", "00000000-0000-0000-0000-000000000000")]
        public void SetImportName_WithVariousFormats_ParsesNameAndIdCorrectly(string importName, string expectedName, string expectedGuid)
        {
            var directory = new Directory();

            directory.SetImportName(importName);

            Assert.Equal(expectedName, directory.Name);
            Assert.Equal(new Guid(expectedGuid), directory.Id);
        }

        [Fact]
        public void SetPath_WithNoParent_UsesOwnId()
        {
            var id = Guid.NewGuid();
            var directory = new Directory { Id = id };

            directory.SetPath();

            Assert.Equal($"{id}/", directory.Path);
        }

        [Fact]
        public void SetPath_WithParentPath_PrependsParentPath()
        {
            var id = Guid.NewGuid();
            var parentId = Guid.NewGuid();
            var directory = new Directory { Id = id };

            directory.SetPath($"{parentId}/");

            Assert.Equal($"{parentId}/{id}/", directory.Path);
        }

        [Theory]
        [InlineData(true, "TestDir__{0}")]
        [InlineData(false, "TestDir")]
        public void GetExportName_WithIncludeIdOption_ReturnsExpectedFormat(bool includeId, string expectedFormat)
        {
            var id = Guid.NewGuid();
            var directory = new Directory { Id = id, Name = "TestDir" };

            var exportName = directory.GetExportName(includeId: includeId);

            var expected = includeId ? string.Format(expectedFormat, id) : expectedFormat;
            Assert.Equal(expected, exportName);
        }

        [Fact]
        public void GetPathNames_WithNoParent_ReturnsOwnName()
        {
            var directory = new Directory { Name = "RootDir" };

            var pathNames = directory.GetPathNames();

            Assert.Equal("RootDir/", pathNames);
        }

        [Fact]
        public void GetPathNames_WithParent_ReturnsFullPath()
        {
            var parent = new Directory { Name = "ParentDir" };
            var child = new Directory { Name = "ChildDir", Parent = parent };

            var pathNames = child.GetPathNames();

            Assert.Equal("ParentDir/ChildDir/", pathNames);
        }

        [Fact]
        public void GetPathNames_WithGrandparent_ReturnsFullHierarchy()
        {
            var grandparent = new Directory { Name = "GrandParent" };
            var parent = new Directory { Name = "Parent", Parent = grandparent };
            var child = new Directory { Name = "Child", Parent = parent };

            var pathNames = child.GetPathNames();

            Assert.Equal("GrandParent/Parent/Child/", pathNames);
        }

        [Fact]
        public void Constructor_WithParentDirectory_SetsProjectIdFromParent()
        {
            var projectId = Guid.NewGuid();
            var parent = new Directory { Id = Guid.NewGuid(), Name = "Parent", ProjectId = projectId };
            parent.SetPath();

            var child = new Directory("Child", parent, Guid.NewGuid());

            Assert.Equal(projectId, child.ProjectId);
            Assert.Equal(parent.Id, child.ParentId);
        }

        [Fact]
        public void Constructor_WithoutParentDirectory_SetsPathFromOwnId()
        {
            var id = Guid.NewGuid();

            var directory = new Directory("TestDir", null, id);

            Assert.Equal("TestDir", directory.Name);
            Assert.Equal(id, directory.Id);
            Assert.Equal($"{id}/", directory.Path);
        }

        [Fact]
        public void PathIds_WithMultipleLevels_ReturnsCorrectGuidArray()
        {
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var id3 = Guid.NewGuid();
            var directory = new Directory { Id = id3, ParentId = id2 };
            directory.SetPath($"{id1}/{id2}/");

            var pathIds = directory.PathIds();

            Assert.Equal(new[] { id1, id2, id3 }, pathIds);
        }
    }
}
