// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Options;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.Zip;
using NSubstitute;
using Xunit;
using Directory = Caster.Api.Domain.Models.Directory;
using File = Caster.Api.Domain.Models.File;

namespace Caster.Api.Tests.Domain.Services
{
    [Trait("Category", "Unit")]
    [Trait("Category", "Archive")]
    public class ArchiveRoundTripTests
    {
        private const string AsciiContent = "# plain ascii\nvariable \"name\" {\n  default = \"value\"\n}\n";
        private const string MultibyteContent = "# régime\nvariable \"ville\" {\n  default = \"日本語 \U0001F389 Ωmega – ok\"\n}\n";

        private const string InstalledVersion = "1.5.7";

        private static ArchiveService NewArchiveService(params string[] installedVersions)
        {
            var terraformService = Substitute.For<ITerraformService>();
            terraformService
                .IsValidVersion(Arg.Any<string>())
                .Returns(call => installedVersions.Contains(call.Arg<string>(), StringComparer.Ordinal));

            return new ArchiveService(terraformService, new TerraformOptions { MaxParallelism = 25 });
        }

        /// <summary>
        /// A Project with one Directory holding a Directory level File and a Workspace with its
        /// own File, with every setting an archive is expected to carry set to a non-default.
        /// </summary>
        private static Project BuildProject(string content)
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

            directory.Files.Add(new File { Name = "main.tf", Content = content });

            var workspace = new Workspace("default", directory)
            {
                TerraformVersion = InstalledVersion,
                Parallelism = 2,
                AzureDestroyFailureThreshold = 5
            };

            workspace.Files.Add(new File { Name = "workspace.tf", Content = content });
            directory.Workspaces.Add(workspace);

            project.Directories.Add(directory);

            return project;
        }

        private static Directory SingleDirectory(Project project)
        {
            return Assert.Single(project.Directories, x => x.Name == "Infrastructure");
        }

        #region Encoding

        [Theory]
        [InlineData(ArchiveType.zip)]
        [InlineData(ArchiveType.tgz)]
        public async Task Multibyte_Content_Survives_A_Round_Trip(ArchiveType archiveType)
        {
            var service = NewArchiveService(InstalledVersion);

            var archive = await service.ArchiveProject(BuildProject(MultibyteContent), archiveType, includeIds: false, includeSettings: true);
            var extracted = service.ExtractProject(archive.Data, archive.Name);

            var directory = SingleDirectory(extracted.Entity);

            Assert.Equal(MultibyteContent, Assert.Single(directory.Files).Content);
            Assert.Equal(MultibyteContent, Assert.Single(Assert.Single(directory.Workspaces).Files).Content);
        }

        [Theory]
        [InlineData(ArchiveType.zip)]
        [InlineData(ArchiveType.tgz)]
        public async Task Ascii_Content_Survives_A_Round_Trip(ArchiveType archiveType)
        {
            var service = NewArchiveService(InstalledVersion);

            var archive = await service.ArchiveProject(BuildProject(AsciiContent), archiveType, includeIds: false, includeSettings: true);
            var extracted = service.ExtractProject(archive.Data, archive.Name);

            var directory = SingleDirectory(extracted.Entity);

            Assert.Equal(AsciiContent, Assert.Single(directory.Files).Content);
            Assert.Equal(AsciiContent, Assert.Single(Assert.Single(directory.Workspaces).Files).Content);
        }

        /// <summary>
        /// File names allow characters outside of ASCII, and they are written into the entry
        /// name, which is encoded separately from the entry's content.
        /// </summary>
        [Theory]
        [InlineData(ArchiveType.zip)]
        [InlineData(ArchiveType.tgz)]
        public async Task Multibyte_File_Names_Survive_A_Round_Trip(ArchiveType archiveType)
        {
            var service = NewArchiveService(InstalledVersion);
            var project = BuildProject(AsciiContent);

            project.Directories.First().Files.Add(new File { Name = "café-日本.tf", Content = AsciiContent });

            var archive = await service.ArchiveProject(project, archiveType, includeIds: false, includeSettings: true);
            var extracted = service.ExtractProject(archive.Data, archive.Name);

            Assert.Contains(SingleDirectory(extracted.Entity).Files, x => x.Name == "café-日本.tf");
        }

        /// <summary>
        /// tar stores the entry size in its header and enforces it, so a declared size taken from
        /// the string's length rather than its encoded length either throws or truncates.
        /// </summary>
        [Fact]
        public async Task Tar_Entry_Sizes_Match_The_Encoded_Content()
        {
            var service = NewArchiveService(InstalledVersion);
            var expectedLength = Encoding.UTF8.GetByteCount(MultibyteContent);

            Assert.NotEqual(MultibyteContent.Length, expectedLength);

            var archive = await service.ArchiveProject(BuildProject(MultibyteContent), ArchiveType.tgz, includeIds: false, includeSettings: true);

            var fileEntries = TarEntries(archive.Data).Where(x => x.Name.EndsWith(".tf", StringComparison.Ordinal)).ToList();

            Assert.Equal(2, fileEntries.Count);
            Assert.All(fileEntries, x => Assert.Equal(expectedLength, x.Size));
        }

        [Fact]
        public void Invalid_Bytes_Do_Not_Throw_And_Are_Replaced()
        {
            var service = NewArchiveService(InstalledVersion);

            // 0xFF is never valid in a UTF-8 sequence
            var stream = BuildZip(("Infrastructure/main.tf", new byte[] { 0x61, 0xFF, 0xFE, 0x62 }));

            var extracted = service.ExtractProject(stream, "upload.zip");

            var content = Assert.Single(SingleDirectory(extracted.Entity).Files).Content;

            Assert.StartsWith("a", content, StringComparison.Ordinal);
            Assert.EndsWith("b", content, StringComparison.Ordinal);
            Assert.Contains('�', content);
        }

        #endregion

        #region Settings

        [Theory]
        [InlineData(ArchiveType.zip)]
        [InlineData(ArchiveType.tgz)]
        public async Task Settings_Survive_A_Round_Trip(ArchiveType archiveType)
        {
            var service = NewArchiveService(InstalledVersion);

            var archive = await service.ArchiveProject(BuildProject(AsciiContent), archiveType, includeIds: false, includeSettings: true);
            var extracted = service.ExtractProject(archive.Data, archive.Name);

            var directory = SingleDirectory(extracted.Entity);

            Assert.Equal(InstalledVersion, directory.TerraformVersion);
            Assert.Equal(4, directory.Parallelism);
            Assert.Equal(3, directory.AzureDestroyFailureThreshold);
            Assert.True(directory.AzureDestroyFailureThresholdEnabled);

            var workspace = Assert.Single(directory.Workspaces);

            Assert.Equal(InstalledVersion, workspace.TerraformVersion);
            Assert.Equal(2, workspace.Parallelism);
            Assert.Equal(5, workspace.AzureDestroyFailureThreshold);

            Assert.Empty(extracted.SkippedSettings);
            Assert.Empty(extracted.Warnings);
        }

        [Fact]
        public async Task Settings_Survive_A_Round_Trip_With_Ids_Included()
        {
            var service = NewArchiveService(InstalledVersion);
            var project = BuildProject(AsciiContent);

            var archive = await service.ArchiveProject(project, ArchiveType.zip, includeIds: true, includeSettings: true);
            var extracted = service.ExtractProject(archive.Data, archive.Name);

            var directory = SingleDirectory(extracted.Entity);

            Assert.Equal(project.Directories.First().Id, directory.Id);
            Assert.Equal(InstalledVersion, directory.TerraformVersion);
            Assert.Equal(InstalledVersion, Assert.Single(directory.Workspaces).TerraformVersion);
        }

        [Fact]
        public async Task Directory_Export_Carries_Child_Settings()
        {
            var service = NewArchiveService(InstalledVersion);
            var project = BuildProject(AsciiContent);
            var root = project.Directories.First();

            var child = new Directory("Child", root)
            {
                TerraformVersion = InstalledVersion,
                Parallelism = 7
            };

            child.Files.Add(new File { Name = "child.tf", Content = AsciiContent });
            root.Children.Add(child);

            var archive = await service.ArchiveDirectory(root, ArchiveType.tgz, includeIds: false, includeSettings: true);
            var extracted = service.ExtractDirectory(archive.Data, archive.Name);

            var extractedChild = Assert.Single(extracted.Entity.Children);

            Assert.Equal("Child", extractedChild.Name);
            Assert.Equal(InstalledVersion, extractedChild.TerraformVersion);
            Assert.Equal(7, extractedChild.Parallelism);
            Assert.Equal(InstalledVersion, Assert.Single(extracted.Entity.Workspaces).TerraformVersion);
        }

        /// <summary>
        /// Every archive produced before the manifest existed lacks one, which is the same shape
        /// as an export with settings turned off.
        /// </summary>
        [Theory]
        [InlineData(ArchiveType.zip)]
        [InlineData(ArchiveType.tgz)]
        public async Task An_Archive_Without_A_Manifest_Imports_As_Before(ArchiveType archiveType)
        {
            var service = NewArchiveService(InstalledVersion);

            var archive = await service.ArchiveProject(BuildProject(AsciiContent), archiveType, includeIds: false, includeSettings: false);
            var extracted = service.ExtractProject(archive.Data, archive.Name);

            var directory = SingleDirectory(extracted.Entity);

            Assert.Null(extracted.Manifest);
            Assert.Empty(extracted.SkippedSettings);
            Assert.Empty(extracted.Warnings);

            Assert.Null(directory.TerraformVersion);
            Assert.Null(directory.Parallelism);
            Assert.Null(directory.AzureDestroyFailureThreshold);

            var workspace = Assert.Single(directory.Workspaces);

            Assert.Null(workspace.TerraformVersion);
            Assert.Null(workspace.Parallelism);
            Assert.Null(workspace.AzureDestroyFailureThreshold);

            // Structure and content are unaffected
            Assert.Equal(AsciiContent, Assert.Single(directory.Files).Content);
            Assert.Equal(AsciiContent, Assert.Single(workspace.Files).Content);
        }

        [Theory]
        [InlineData(ArchiveType.zip)]
        [InlineData(ArchiveType.tgz)]
        public async Task The_Manifest_Is_Never_Extracted_As_Content(ArchiveType archiveType)
        {
            var service = NewArchiveService(InstalledVersion);

            var archive = await service.ArchiveProject(BuildProject(AsciiContent), archiveType, includeIds: false, includeSettings: true);
            var extracted = service.ExtractProject(archive.Data, archive.Name);

            Assert.DoesNotContain(extracted.Entity.Directories, x => x.Name.Contains("__Caster__", StringComparison.Ordinal));
            Assert.DoesNotContain(extracted.Entity.Directories, x => x.Name == "manifest.json");

            Assert.All(extracted.Entity.Directories, directory =>
            {
                Assert.DoesNotContain(directory.Files, x => x.Name == "manifest.json");
                Assert.DoesNotContain(directory.Children, x => x.Name == "manifest.json");
            });
        }

        [Fact]
        public async Task A_Version_The_System_Does_Not_Have_Is_Dropped_And_Reported()
        {
            // Nothing is installed, so the exported version cannot be honored
            var service = NewArchiveService();

            var archive = await service.ArchiveProject(BuildProject(AsciiContent), ArchiveType.zip, includeIds: false, includeSettings: true);
            var extracted = service.ExtractProject(archive.Data, archive.Name);

            var directory = SingleDirectory(extracted.Entity);
            var workspace = Assert.Single(directory.Workspaces);

            // Unpinned, so the system default applies, rather than failing the import
            Assert.Null(directory.TerraformVersion);
            Assert.Null(workspace.TerraformVersion);

            // The settings that are still valid are applied
            Assert.Equal(4, directory.Parallelism);
            Assert.Equal(2, workspace.Parallelism);

            Assert.Equal(2, extracted.SkippedSettings.Count);
            Assert.All(extracted.SkippedSettings, x => Assert.Contains(InstalledVersion, x, StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("../../../../etc")]
        [InlineData("..")]
        [InlineData("/etc/passwd")]
        [InlineData("1.5.7 --flag")]
        [InlineData("latest;rm -rf /")]
        public void A_Crafted_Version_Is_Rejected_Before_It_Reaches_A_Path_Or_Image(string version)
        {
            // Worst case: a system where every probed version looks available
            var terraformService = Substitute.For<ITerraformService>();
            terraformService.IsValidVersion(Arg.Any<string>()).Returns(true);

            var service = new ArchiveService(terraformService, new TerraformOptions { MaxParallelism = 25 });

            var manifest = $@"{{
                ""schemaVersion"": 1,
                ""source"": {{ ""type"": ""project"", ""name"": ""Range Alpha"" }},
                ""directories"": {{ ""Infrastructure"": {{ ""terraformVersion"": ""{version.Replace("\\", "\\\\")}"" }} }},
                ""workspaces"": {{ ""Infrastructure/__Workspaces__/default"": {{ ""terraformVersion"": ""{version.Replace("\\", "\\\\")}"" }} }}
            }}";

            var stream = BuildZip(
                ("__Caster__/manifest.json", Encoding.UTF8.GetBytes(manifest)),
                ("Infrastructure/main.tf", Encoding.UTF8.GetBytes(AsciiContent)),
                ("Infrastructure/__Workspaces__/default/workspace.tf", Encoding.UTF8.GetBytes(AsciiContent)));

            var extracted = service.ExtractProject(stream, "upload.zip");

            var directory = SingleDirectory(extracted.Entity);

            Assert.Null(directory.TerraformVersion);
            Assert.Null(Assert.Single(directory.Workspaces).TerraformVersion);
            Assert.Equal(2, extracted.SkippedSettings.Count);
        }

        [Fact]
        public void An_Out_Of_Range_Parallelism_Is_Dropped_And_Reported()
        {
            var service = NewArchiveService(InstalledVersion);

            var manifest = @"{
                ""schemaVersion"": 1,
                ""directories"": { ""Infrastructure"": { ""parallelism"": 9000 } }
            }";

            var stream = BuildZip(
                ("__Caster__/manifest.json", Encoding.UTF8.GetBytes(manifest)),
                ("Infrastructure/main.tf", Encoding.UTF8.GetBytes(AsciiContent)));

            var extracted = service.ExtractProject(stream, "upload.zip");

            Assert.Null(SingleDirectory(extracted.Entity).Parallelism);
            Assert.Single(extracted.SkippedSettings);
        }

        [Fact]
        public void An_Out_Of_Range_Azure_Threshold_Is_Dropped_And_Reported()
        {
            var service = NewArchiveService(InstalledVersion);

            var manifest = @"{
                ""schemaVersion"": 1,
                ""directories"": { ""Infrastructure"": { ""azureDestroyFailureThreshold"": 11, ""parallelism"": 4 } }
            }";

            var stream = BuildZip(
                ("__Caster__/manifest.json", Encoding.UTF8.GetBytes(manifest)),
                ("Infrastructure/main.tf", Encoding.UTF8.GetBytes(AsciiContent)));

            var extracted = service.ExtractProject(stream, "upload.zip");

            var directory = SingleDirectory(extracted.Entity);
            Assert.Null(directory.AzureDestroyFailureThreshold);
            Assert.Equal(4, directory.Parallelism);
            Assert.Equal(["Directory 'Infrastructure': Azure destroy failure threshold of 11 is outside of the allowed range and was ignored."], extracted.SkippedSettings);
        }

        [Fact]
        public void Settings_For_A_Directory_The_Archive_Does_Not_Hold_Are_Skipped_And_Reported()
        {
            var service = NewArchiveService(InstalledVersion);

            var manifest = @"{
                ""schemaVersion"": 1,
                ""directories"": { ""Missing"": { ""parallelism"": 4 } }
            }";

            var stream = BuildZip(
                ("__Caster__/manifest.json", Encoding.UTF8.GetBytes(manifest)),
                ("Infrastructure/main.tf", Encoding.UTF8.GetBytes(AsciiContent)));

            var extracted = service.ExtractProject(stream, "upload.zip");

            Assert.Null(SingleDirectory(extracted.Entity).Parallelism);
            Assert.Equal(["Directory 'Missing': not present in the archive's contents."], extracted.SkippedSettings);
        }

        [Fact]
        public void Settings_For_A_Workspace_The_Archive_Does_Not_Hold_Are_Skipped_And_Reported()
        {
            var service = NewArchiveService(InstalledVersion);

            var manifest = @"{
                ""schemaVersion"": 1,
                ""workspaces"": { ""Infrastructure/__Workspaces__/missing"": { ""parallelism"": 2 } }
            }";

            var stream = BuildZip(
                ("__Caster__/manifest.json", Encoding.UTF8.GetBytes(manifest)),
                ("Infrastructure/main.tf", Encoding.UTF8.GetBytes(AsciiContent)),
                ("Infrastructure/__Workspaces__/default/workspace.tf", Encoding.UTF8.GetBytes(AsciiContent)));

            var extracted = service.ExtractProject(stream, "upload.zip");

            Assert.Null(Assert.Single(SingleDirectory(extracted.Entity).Workspaces).Parallelism);
            Assert.Equal(["Workspace 'Infrastructure/__Workspaces__/missing': not present in the archive's contents."], extracted.SkippedSettings);
        }

        /// <summary>A manifest from a newer Caster is still applied as far as this one reads it, with a warning.</summary>
        [Fact]
        public void A_Manifest_Of_A_Newer_Schema_Is_Applied_With_A_Warning()
        {
            var service = NewArchiveService(InstalledVersion);

            var manifest = @"{
                ""schemaVersion"": 2,
                ""directories"": { ""Infrastructure"": { ""parallelism"": 4 } }
            }";

            var stream = BuildZip(
                ("__Caster__/manifest.json", Encoding.UTF8.GetBytes(manifest)),
                ("Infrastructure/main.tf", Encoding.UTF8.GetBytes(AsciiContent)));

            var extracted = service.ExtractProject(stream, "upload.zip");

            Assert.Equal(4, SingleDirectory(extracted.Entity).Parallelism);
            Assert.Equal(["The archive's settings manifest uses schema version 2, which is newer than this system supports. Some settings may not be restored."], extracted.Warnings);
            Assert.Empty(extracted.SkippedSettings);
        }

        [Fact]
        public void An_Unreadable_Manifest_Does_Not_Fail_The_Import()
        {
            var service = NewArchiveService(InstalledVersion);

            var stream = BuildZip(
                ("__Caster__/manifest.json", Encoding.UTF8.GetBytes("{ this is not json")),
                ("Infrastructure/main.tf", Encoding.UTF8.GetBytes(AsciiContent)));

            var extracted = service.ExtractProject(stream, "upload.zip");

            Assert.Null(extracted.Manifest);
            Assert.Equal(AsciiContent, Assert.Single(SingleDirectory(extracted.Entity).Files).Content);
            Assert.Single(extracted.Warnings);
        }

        /// <summary>
        /// A Directory export used to root every entry at "/" and carried no manifest. Archives
        /// in that shape must still import exactly as they did.
        /// </summary>
        [Fact]
        public void A_Legacy_Shaped_Archive_Imports_Unchanged()
        {
            var service = NewArchiveService(InstalledVersion);

            var stream = BuildZip(
                ("/main.tf", Encoding.UTF8.GetBytes(AsciiContent)),
                ("/__Workspaces__/default/", []),
                ("/__Workspaces__/default/workspace.tf", Encoding.UTF8.GetBytes(AsciiContent)),
                ("/Child/child.tf", Encoding.UTF8.GetBytes(AsciiContent)));

            var extracted = service.ExtractDirectory(stream, "Infrastructure.zip");

            Assert.Null(extracted.Manifest);
            Assert.Empty(extracted.SkippedSettings);
            Assert.Empty(extracted.Warnings);

            Assert.Equal(AsciiContent, Assert.Single(extracted.Entity.Files, x => x.Name == "main.tf").Content);

            var workspace = Assert.Single(extracted.Entity.Workspaces);
            Assert.Equal("default", workspace.Name);
            Assert.Equal(AsciiContent, Assert.Single(workspace.Files).Content);
            Assert.Null(workspace.TerraformVersion);

            var child = Assert.Single(extracted.Entity.Children);
            Assert.Equal("Child", child.Name);
            Assert.Equal(AsciiContent, Assert.Single(child.Files).Content);
        }

        [Fact]
        public async Task The_Manifest_Records_The_Source_Name()
        {
            var service = NewArchiveService(InstalledVersion);

            var archive = await service.ArchiveProject(BuildProject(AsciiContent), ArchiveType.zip, includeIds: false, includeSettings: true);
            var extracted = service.ExtractProject(archive.Data, archive.Name);

            Assert.NotNull(extracted.Manifest);
            Assert.Equal("project", extracted.Manifest.Source.Type);
            Assert.Equal("Range Alpha", extracted.Manifest.Source.Name);
        }

        #endregion

        /// <summary>The entries of a tar.gz archive as its headers declare them, read to the end.</summary>
        private static List<TarEntry> TarEntries(Stream archive)
        {
            using var gzipStream = new GZipInputStream(archive);
            using var tarStream = new TarInputStream(gzipStream, Encoding.UTF8);

            var entries = new List<TarEntry>();

            while (tarStream.GetNextEntry() is TarEntry entry)
            {
                entries.Add(entry);
            }

            return entries;
        }

        private static Stream BuildZip(params (string Name, byte[] Content)[] entries)
        {
            var stream = new MemoryStream();

            using (var zipStream = new ZipOutputStream(stream) { IsStreamOwner = false })
            {
                foreach (var entry in entries)
                {
                    zipStream.PutNextEntry(new ZipEntry(entry.Name));
                    zipStream.Write(entry.Content, 0, entry.Content.Length);
                    zipStream.CloseEntry();
                }
            }

            stream.Position = 0;

            return stream;
        }
    }
}
