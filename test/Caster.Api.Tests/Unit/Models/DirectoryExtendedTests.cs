// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;
using Directory = Caster.Api.Domain.Models.Directory;
using File = Caster.Api.Domain.Models.File;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace Caster.Api.Tests.Unit.Models
{
    [Category("Unit")]
    [Category("Directory")]
    public class DirectoryExtendedTests
    {
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
        public async Task GetExportName_WithIncludeId_ReturnsNameAndId()
        {
            var id = Guid.NewGuid();
            var directory = new Directory { Id = id, Name = "TestDir" };

            var exportName = directory.GetExportName(includeId: true);

            await Assert.That(exportName).IsEqualTo($"TestDir__{id}");
        }

        [Test]
        public async Task GetExportName_WithoutIncludeId_ReturnsNameOnly()
        {
            var directory = new Directory { Name = "TestDir" };

            var exportName = directory.GetExportName(includeId: false);

            await Assert.That(exportName).IsEqualTo("TestDir");
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
        public async Task Constructor_WithParent_SetsProjectIdFromParent()
        {
            var projectId = Guid.NewGuid();
            var parent = new Directory { Id = Guid.NewGuid(), Name = "Parent", ProjectId = projectId };
            parent.SetPath();

            var child = new Directory("Child", parent, Guid.NewGuid());

            await Assert.That(child.ProjectId).IsEqualTo(projectId);
            await Assert.That(child.ParentId).IsEqualTo(parent.Id);
        }

        [Test]
        public async Task Constructor_WithoutParent_SetsPathFromOwnId()
        {
            var id = Guid.NewGuid();

            var directory = new Directory("TestDir", null, id);

            await Assert.That(directory.Name).IsEqualTo("TestDir");
            await Assert.That(directory.Id).IsEqualTo(id);
            await Assert.That(directory.Path).IsEqualTo($"{id}/");
        }

        [Test]
        public async Task SetImportName_WithValidGuid_SetsIdAndName()
        {
            var directory = new Directory();
            var guid = Guid.NewGuid();

            directory.SetImportName($"MyDir__{guid}");

            await Assert.That(directory.Name).IsEqualTo("MyDir");
            await Assert.That(directory.Id).IsEqualTo(guid);
        }

        [Test]
        public async Task SetImportName_WithoutGuid_SetsNameOnly()
        {
            var directory = new Directory();

            directory.SetImportName("SimpleDir");

            await Assert.That(directory.Name).IsEqualTo("SimpleDir");
            await Assert.That(directory.Id).IsEqualTo(Guid.Empty);
        }

        [Test]
        public async Task SetImportName_WithDoubleUnderscore_InMiddleButInvalidGuid_KeepsFullName()
        {
            var directory = new Directory();

            directory.SetImportName("MyDir__not-a-guid");

            await Assert.That(directory.Name).IsEqualTo("MyDir__not-a-guid");
            await Assert.That(directory.Id).IsEqualTo(Guid.Empty);
        }

        [Test]
        public async Task PathIds_ReturnsCorrectGuidArray()
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
