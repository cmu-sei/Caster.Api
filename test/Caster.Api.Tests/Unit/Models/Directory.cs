// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;
using Directory = Caster.Api.Domain.Models.Directory;
using File = Caster.Api.Domain.Models.File;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace Caster.Api.Tests.Unit
{
    [Category("Unit")]
    [Category("Directory")]
    public class DirectoryUnitTest
    {
        public DirectoryUnitTest()
        {
        }

        [Test]
        public async Task Test_Directory_Path_To_List()
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

            var expectedPathIds = new Guid[] {greatGrandparentId,grandparentId,parentId,id};
            var pathIds = directory.PathIds();

            await Assert.That(pathIds).IsEquivalentTo(expectedPathIds);
        }

        [Test]
        public async Task Test_Directory_Set_Import_Name()
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

            await Assert.That(dir1.Name).IsEqualTo("DirectoryWithId");
            await Assert.That(dir1.Id).IsEqualTo(new Guid("b7ef25e6-555e-41c9-88d7-22078d3a13c1"));

            await Assert.That(dir2.Name).IsEqualTo("Directory");
            await Assert.That(dir2.Id).IsEqualTo(Guid.Empty);

            await Assert.That(dir3.Name).IsEqualTo("DirectoryWithInvalidId__b7ef25e6-555e-41c9-88d7822078d3a13c1");
            await Assert.That(dir3.Id).IsEqualTo(Guid.Empty);

        }
    }
}
