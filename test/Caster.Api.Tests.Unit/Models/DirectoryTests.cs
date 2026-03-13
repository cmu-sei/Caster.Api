// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;
using Xunit;

namespace Caster.Api.Tests.Unit.Models
{
    [Trait("Category", "Unit")]
    [Trait("Category", "Directory")]
    public class DirectoryTests
    {
        [Fact]
        public void Test_Directory_Path_To_List()
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

        [Fact]
        public void Test_Directory_Set_Import_Name()
        {
            string nameWithGuid = "DirectoryWithId__b7ef25e6-555e-41c9-88d7-22078d3a13c1";
            string nameWithoutGuid = "Directory";
            string nameWithInvalidGuid = "DirectoryWithInvalidId__b7ef25e6-555e-41c9-88d7822078d3a13c1";

            Directory dir1 = new Directory();
            Directory dir2 = new Directory();
            Directory dir3 = new Directory();

            dir1.SetImportName(nameWithGuid);
            dir2.SetImportName(nameWithoutGuid);
            dir3.SetImportName(nameWithInvalidGuid);

            Assert.Equal("DirectoryWithId", dir1.Name);
            Assert.Equal(new Guid("b7ef25e6-555e-41c9-88d7-22078d3a13c1"), dir1.Id);

            Assert.Equal("Directory", dir2.Name);
            Assert.Equal(Guid.Empty, dir2.Id);

            Assert.Equal("DirectoryWithInvalidId__b7ef25e6-555e-41c9-88d7822078d3a13c1", dir3.Name);
            Assert.Equal(Guid.Empty, dir3.Id);
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

        [Fact]
        public void GetExportName_WithIncludeId_ReturnsNameAndId()
        {
            var id = Guid.NewGuid();
            var directory = new Directory { Id = id, Name = "TestDir" };

            var exportName = directory.GetExportName(includeId: true);

            Assert.Equal($"TestDir__{id}", exportName);
        }

        [Fact]
        public void GetExportName_WithoutIncludeId_ReturnsNameOnly()
        {
            var directory = new Directory { Name = "TestDir" };

            var exportName = directory.GetExportName(includeId: false);

            Assert.Equal("TestDir", exportName);
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
        public void Constructor_WithParent_SetsProjectIdFromParent()
        {
            var projectId = Guid.NewGuid();
            var parent = new Directory { Id = Guid.NewGuid(), Name = "Parent", ProjectId = projectId };
            parent.SetPath();

            var child = new Directory("Child", parent, Guid.NewGuid());

            Assert.Equal(projectId, child.ProjectId);
            Assert.Equal(parent.Id, child.ParentId);
        }

        [Fact]
        public void Constructor_WithoutParent_SetsPathFromOwnId()
        {
            var id = Guid.NewGuid();

            var directory = new Directory("TestDir", null, id);

            Assert.Equal("TestDir", directory.Name);
            Assert.Equal(id, directory.Id);
            Assert.Equal($"{id}/", directory.Path);
        }

        [Fact]
        public void SetImportName_WithValidGuid_SetsIdAndName()
        {
            var directory = new Directory();
            var guid = Guid.NewGuid();

            directory.SetImportName($"MyDir__{guid}");

            Assert.Equal("MyDir", directory.Name);
            Assert.Equal(guid, directory.Id);
        }

        [Fact]
        public void SetImportName_WithoutGuid_SetsNameOnly()
        {
            var directory = new Directory();

            directory.SetImportName("SimpleDir");

            Assert.Equal("SimpleDir", directory.Name);
            Assert.Equal(Guid.Empty, directory.Id);
        }

        [Fact]
        public void SetImportName_WithDoubleUnderscore_InMiddleButInvalidGuid_KeepsFullName()
        {
            var directory = new Directory();

            directory.SetImportName("MyDir__not-a-guid");

            Assert.Equal("MyDir__not-a-guid", directory.Name);
            Assert.Equal(Guid.Empty, directory.Id);
        }

        [Fact]
        public void PathIds_ReturnsCorrectGuidArray()
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
