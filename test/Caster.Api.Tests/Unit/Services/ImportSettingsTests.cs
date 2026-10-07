// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;
using Directory = Caster.Api.Domain.Models.Directory;
using File = Caster.Api.Domain.Models.File;

namespace Caster.Api.Tests.Unit.Services
{
    [Trait("Category", "Unit")]
    [Trait("Category", "Archive")]
    public class ImportSettingsTests
    {
        private const string Content = "variable \"name\" {\n  default = \"value\"\n}\n";
        private const string InstalledVersion = "1.5.7";

        private static ArchiveService NewArchiveService(params string[] installedVersions)
        {
            var terraformService = Substitute.For<ITerraformService>();
            terraformService
                .IsValidVersion(Arg.Any<string>())
                .Returns(call => installedVersions.Contains(call.Arg<string>(), StringComparer.Ordinal));

            return new ArchiveService(terraformService, new TerraformOptions { MaxParallelism = 25 });
        }

        private static CasterContext NewContext()
        {
            return new CasterContext(new DbContextOptionsBuilder<CasterContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        }

        private static ImportService NewImportService(CasterContext db)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())]));

            var identityResolver = Substitute.For<IIdentityResolver>();
            identityResolver.GetClaimsPrincipal().Returns(principal);

            return new ImportService(
                Substitute.For<ILockService>(),
                db,
                identityResolver,
                Substitute.For<ICasterAuthorizationService>());
        }

        private static Project BuildProject()
        {
            var project = new Project("Range Alpha");

            var directory = new Directory("Infrastructure")
            {
                ProjectId = project.Id,
                TerraformVersion = InstalledVersion,
                Parallelism = 4,
                AzureDestroyFailureThresholdEnabled = true,
                AzureDestroyFailureThreshold = 3
            };

            directory.Files.Add(new File { Name = "main.tf", Content = Content });

            var workspace = new Workspace("default", directory)
            {
                TerraformVersion = InstalledVersion,
                Parallelism = 2,
                AzureDestroyFailureThreshold = 5
            };

            workspace.Files.Add(new File { Name = "workspace.tf", Content = Content });
            directory.Workspaces.Add(workspace);

            project.Directories.Add(directory);

            return project;
        }

        private static async Task<Project> ImportInto(ArchiveService archiveService, Project source, Project target, CasterContext db)
        {
            var archive = await archiveService.ArchiveProject(source, ArchiveType.tgz, includeIds: false, includeSettings: true);
            var extracted = archiveService.ExtractProject(archive.Data, archive.Name);

            await NewImportService(db).ImportProject(target, extracted.Entity, preserveIds: false, CancellationToken.None);
            await db.SaveChangesAsync();

            return target;
        }

        [Fact]
        public async Task Import_Into_A_New_Project_Preserves_Settings()
        {
            var archiveService = NewArchiveService(InstalledVersion);

            using var db = NewContext();

            var target = new Project("Range Beta");
            db.Projects.Add(target);
            await db.SaveChangesAsync();

            await ImportInto(archiveService, BuildProject(), target, db);

            var directory = Assert.Single(target.Directories);

            Assert.Equal("Infrastructure", directory.Name);
            Assert.Equal(InstalledVersion, directory.TerraformVersion);
            Assert.Equal(4, directory.Parallelism);
            Assert.Equal(3, directory.AzureDestroyFailureThreshold);
            Assert.True(directory.AzureDestroyFailureThresholdEnabled);

            var workspace = Assert.Single(directory.Workspaces);

            Assert.Equal("default", workspace.Name);
            Assert.Equal(InstalledVersion, workspace.TerraformVersion);
            Assert.Equal(2, workspace.Parallelism);
            Assert.Equal(5, workspace.AzureDestroyFailureThreshold);

            // Environment specific values never travel with an archive
            Assert.Null(workspace.HostId);
            Assert.False(workspace.DynamicHost);
            Assert.Null(workspace.State);
            Assert.Null(workspace.StateBackup);
        }

        [Fact]
        public async Task Import_Does_Not_Reconfigure_Entities_That_Already_Exist()
        {
            var archiveService = NewArchiveService(InstalledVersion);

            using var db = NewContext();

            var target = new Project("Range Beta");

            var existingDirectory = new Directory("Infrastructure")
            {
                ProjectId = target.Id,
                TerraformVersion = "0.14.0",
                Parallelism = 9
            };

            var existingWorkspace = new Workspace("default", existingDirectory)
            {
                TerraformVersion = "0.14.0",
                Parallelism = 9
            };

            existingDirectory.Workspaces.Add(existingWorkspace);
            target.Directories.Add(existingDirectory);

            db.Projects.Add(target);
            await db.SaveChangesAsync();

            await ImportInto(archiveService, BuildProject(), target, db);

            var directory = Assert.Single(target.Directories);

            Assert.Equal("0.14.0", directory.TerraformVersion);
            Assert.Equal(9, directory.Parallelism);

            var workspace = Assert.Single(directory.Workspaces);

            Assert.Equal("0.14.0", workspace.TerraformVersion);
            Assert.Equal(9, workspace.Parallelism);

            // The content still imported
            Assert.Contains(directory.Files, x => x.Name == "main.tf");
        }

        [Fact]
        public async Task Import_Of_An_Archive_Without_A_Manifest_Leaves_New_Entities_Unpinned()
        {
            var archiveService = NewArchiveService(InstalledVersion);

            using var db = NewContext();

            var target = new Project("Range Beta");
            db.Projects.Add(target);
            await db.SaveChangesAsync();

            var archive = await archiveService.ArchiveProject(BuildProject(), ArchiveType.tgz, includeIds: false, includeSettings: false);
            var extracted = archiveService.ExtractProject(archive.Data, archive.Name);

            await NewImportService(db).ImportProject(target, extracted.Entity, preserveIds: false, CancellationToken.None);
            await db.SaveChangesAsync();

            var directory = Assert.Single(target.Directories);
            var workspace = Assert.Single(directory.Workspaces);

            Assert.Null(directory.TerraformVersion);
            Assert.Null(workspace.TerraformVersion);
            Assert.Null(workspace.Parallelism);

            Assert.Contains(directory.Files, x => x.Name == "main.tf");
            Assert.Contains(workspace.Files, x => x.Name == "workspace.tf");
        }

        [Fact]
        public void Skipped_Settings_Are_Surfaced_In_The_Import_Responses()
        {
            var importResult = new ImportResult
            {
                LockedFiles = [],
                SkippedSettings = ["Workspace 'a': skipped"],
                Warnings = ["exported from somewhere else"]
            };

            var mapper = new AutoMapper.MapperConfiguration(cfg =>
            {
                cfg.AddProfile<Caster.Api.Features.Projects.MappingProfile>();
                cfg.AddProfile<Caster.Api.Features.Directories.MappingProfile>();
            }).CreateMapper();

            var projectResult = mapper.Map<Caster.Api.Features.Projects.Import.ImportProjectResult>(importResult);
            var directoryResult = mapper.Map<Caster.Api.Features.Directories.Import.ImportDirectoryResult>(importResult);

            Assert.Equal(importResult.SkippedSettings, projectResult.SkippedSettings);
            Assert.Equal(importResult.Warnings, projectResult.Warnings);
            Assert.Equal(importResult.SkippedSettings, directoryResult.SkippedSettings);
            Assert.Equal(importResult.Warnings, directoryResult.Warnings);
        }

        [Fact]
        public async Task A_Version_The_System_Does_Not_Have_Lands_Unpinned()
        {
            // The target has no Terraform versions installed
            var archiveService = NewArchiveService();

            using var db = NewContext();

            var target = new Project("Range Beta");
            db.Projects.Add(target);
            await db.SaveChangesAsync();

            var archive = await archiveService.ArchiveProject(BuildProject(), ArchiveType.tgz, includeIds: false, includeSettings: true);
            var extracted = archiveService.ExtractProject(archive.Data, archive.Name);

            await NewImportService(db).ImportProject(target, extracted.Entity, preserveIds: false, CancellationToken.None);
            await db.SaveChangesAsync();

            var directory = Assert.Single(target.Directories);
            var workspace = Assert.Single(directory.Workspaces);

            Assert.Null(directory.TerraformVersion);
            Assert.Null(workspace.TerraformVersion);

            // Still imported, with the omission reported
            Assert.Equal(4, directory.Parallelism);
            Assert.NotEmpty(extracted.SkippedSettings);
        }
    }
}
