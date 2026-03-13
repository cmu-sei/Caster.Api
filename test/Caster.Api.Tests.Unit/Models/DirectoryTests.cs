// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;
using TUnit.Core;

namespace Caster.Api.Tests.Unit.Models
{
    [Category("Unit")]
    public class DirectoryTests
    {
        [Test]
        public async Task PathIds_WithNestedHierarchy_ReturnsAllIds()
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

            await Assert.That(pathIds).IsEquivalentTo(expectedPathIds);
        }

        [Test]
        [Arguments("DirectoryWithId__b7ef25e6-555e-41c9-88d7-22078d3a13c1", "DirectoryWithId", "b7ef25e6-555e-41c9-88d7-22078d3a13c1")]
        [Arguments("Directory", "Directory", "00000000-0000-0000-0000-000000000000")]
        [Arguments("DirectoryWithInvalidId__b7ef25e6-555e-41c9-88d7822078d3a13c1", "DirectoryWithInvalidId__b7ef25e6-555e-41c9-88d7822078d3a13c1", "00000000-0000-0000-0000-000000000000")]
        public async Task SetImportName_WithVariousFormats_ParsesNameAndIdCorrectly(string importName, string expectedName, string expectedGuid)
        {
            var directory = new Directory();

            directory.SetImportName(importName);

            await Assert.That(directory.Name).IsEqualTo(expectedName);
            await Assert.That(directory.Id).IsEqualTo(new Guid(expectedGuid));
        }

        [Test]
        public async Task SetPath_WithNoParent_UsesOwnId()
        {
            var id = Guid.NewGuid();
            var directory = new Directory { Id = id };

            directory.SetPath();

            await Assert.That(directory.Path).IsEqualTo($"{id}/");
        }

        [Test]
        public async Task SetPath_WithParentPath_PrependsParentPath()
        {
            var id = Guid.NewGuid();
            var parentId = Guid.NewGuid();
            var directory = new Directory { Id = id };

            directory.SetPath($"{parentId}/");

            await Assert.That(directory.Path).IsEqualTo($"{parentId}/{id}/");
        }

        [Test]
        [Arguments(true, "TestDir__{0}")]
        [Arguments(false, "TestDir")]
        public async Task GetExportName_WithIncludeIdOption_ReturnsExpectedFormat(bool includeId, string expectedFormat)
        {
            var id = Guid.NewGuid();
            var directory = new Directory { Id = id, Name = "TestDir" };

            var exportName = directory.GetExportName(includeId: includeId);

            var expected = includeId ? string.Format(expectedFormat, id) : expectedFormat;
            await Assert.That(exportName).IsEqualTo(expected);
        }

        [Test]
        public async Task GetPathNames_WithNoParent_ReturnsOwnName()
        {
            var directory = new Directory { Name = "RootDir" };

            var pathNames = directory.GetPathNames();

            await Assert.That(pathNames).IsEqualTo("RootDir/");
        }

        [Test]
        public async Task GetPathNames_WithParent_ReturnsFullPath()
        {
            var parent = new Directory { Name = "ParentDir" };
            var child = new Directory { Name = "ChildDir", Parent = parent };

            var pathNames = child.GetPathNames();

            await Assert.That(pathNames).IsEqualTo("ParentDir/ChildDir/");
        }

        [Test]
        public async Task GetPathNames_WithGrandparent_ReturnsFullHierarchy()
        {
            var grandparent = new Directory { Name = "GrandParent" };
            var parent = new Directory { Name = "Parent", Parent = grandparent };
            var child = new Directory { Name = "Child", Parent = parent };

            var pathNames = child.GetPathNames();

            await Assert.That(pathNames).IsEqualTo("GrandParent/Parent/Child/");
        }

        [Test]
        public async Task Constructor_WithParentDirectory_SetsProjectIdFromParent()
        {
            var projectId = Guid.NewGuid();
            var parent = new Directory { Id = Guid.NewGuid(), Name = "Parent", ProjectId = projectId };
            parent.SetPath();

            var child = new Directory("Child", parent, Guid.NewGuid());

            await Assert.That(child.ProjectId).IsEqualTo(projectId);
            await Assert.That(child.ParentId).IsEqualTo(parent.Id);
        }

        [Test]
        public async Task Constructor_WithoutParentDirectory_SetsPathFromOwnId()
        {
            var id = Guid.NewGuid();

            var directory = new Directory("TestDir", null, id);

            await Assert.That(directory.Name).IsEqualTo("TestDir");
            await Assert.That(directory.Id).IsEqualTo(id);
            await Assert.That(directory.Path).IsEqualTo($"{id}/");
        }

        [Test]
        public async Task PathIds_WithMultipleLevels_ReturnsCorrectGuidArray()
        {
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var id3 = Guid.NewGuid();
            var directory = new Directory { Id = id3, ParentId = id2 };
            directory.SetPath($"{id1}/{id2}/");

            var pathIds = directory.PathIds();

            await Assert.That(pathIds).IsEquivalentTo(new[] { id1, id2, id3 });
        }
    }
}
