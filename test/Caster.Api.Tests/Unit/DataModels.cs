// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Directory = Caster.Api.Domain.Models.Directory;
using File = Caster.Api.Domain.Models.File;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace Caster.Api.Tests.Unit
{
    [Category("Unit")]
    [Category("DataModels")]
    public class DataModelsUnitTest
    {
        private readonly CasterContext _context;

        public DataModelsUnitTest()
        {
            var builder = new DbContextOptionsBuilder<CasterContext>();
            builder.UseInMemoryDatabase("caster_test");
            _context = new CasterContext(builder.Options);
        }

        [Test]
        public async Task Test_Project_InsertAndRetrieve()
        {
            var projectInsert = new Project();

            _context.Projects.Add(projectInsert);
            _context.SaveChanges();

            var projectRetrieve = _context.Projects.SingleOrDefault(item => item.Id == projectInsert.Id);
            await Assert.That(projectRetrieve).IsNotNull();
        }

        [Test]
        public async Task Test_Directory_InsertAndRetrieve()
        {
            var directoryInsert = new Directory();

            _context.Directories.Add(directoryInsert);
            _context.SaveChanges();

            var directoryRetrieve = _context.Directories.SingleOrDefault(item => item.Id == directoryInsert.Id);
            await Assert.That(directoryRetrieve).IsNotNull();
        }

        [Test]
        public async Task Test_File_InsertAndRetrieve()
        {
            var fileInsert = new File();

            _context.Files.Add(fileInsert);
            _context.SaveChanges();

            var fileRetrieve = _context.Files.SingleOrDefault(item => item.Id == fileInsert.Id);
            await Assert.That(fileRetrieve).IsNotNull();
        }

        [Test]
        public async Task Test_ProjectAndDirectory_Relation()
        {
            var projectInsert = new Project{};
            var directoryInsert = new Directory {Project = projectInsert};

            _context.Projects.Add(projectInsert);
            _context.Directories.Add(directoryInsert);
            _context.SaveChanges();
            var projectRetrieve = _context.Projects.Single(item => item.Id == projectInsert.Id);
            var directoryRetrieve = _context.Directories.Single(item => item.Id == directoryInsert.Id);

            await Assert.That(projectRetrieve.Directories).Contains(directoryRetrieve);
            await Assert.That(directoryRetrieve.Project).IsEqualTo(projectRetrieve);
        }

        [Test]
        public async Task Test_DirectoryAndFile_Relation()
        {
            var directoryInsert = new Directory();
            var fileInsert = new File {Directory = directoryInsert};

            _context.Directories.Add(directoryInsert);
            _context.Files.Add(fileInsert);
            _context.SaveChanges();
            var directoryRetrieve = _context.Directories.Single(item => item.Id == directoryInsert.Id);
            var fileRetrieve = _context.Files.Single(item => item.Id == fileInsert.Id);

            await Assert.That(directoryRetrieve.Files).Contains(fileRetrieve);
            await Assert.That(fileRetrieve.Directory).IsEqualTo(directoryRetrieve);
        }
    }
}
