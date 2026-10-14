// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Options;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.Zip;

namespace Caster.Api.Domain.Services
{
    public interface IArchiveService
    {
        Task<ArchiveResult> ArchiveProject(Project project, ArchiveType type, bool includeIds, bool includeSettings);
        Task<ArchiveResult> ArchiveDirectory(Directory directory, ArchiveType type, bool includeIds, bool includeSettings);
        ArchiveExtractResult<Directory> ExtractDirectory(System.IO.Stream stream, string filename);
        ArchiveExtractResult<Project> ExtractProject(System.IO.Stream stream, string filename);
    }

    public class ArchiveService(ITerraformService terraformService, TerraformOptions terraformOptions) : IArchiveService
    {
        /// <summary>
        /// Reserved path segment that separates a Directory's Workspaces from its own Files.
        /// </summary>
        public const string WorkspacesEntryName = "__Workspaces__";

        /// <summary>
        /// Reserved root path segment used for Caster's own archive metadata. Nothing under it is
        /// ever materialized as a File, Directory, or Workspace on Import.
        /// </summary>
        public const string ReservedEntryName = "__Caster__";

        /// <summary>
        /// The single manifest entry carrying Directory and Workspace settings.
        /// </summary>
        public const string ManifestEntryName = ReservedEntryName + "/manifest.json";

        /// <summary>
        /// File content is stored as text. Reading and writing must agree on an encoding or
        /// anything outside of ASCII is silently destroyed. Invalid bytes are replaced rather
        /// than thrown on, so a malformed upload cannot fail part way through an Import and
        /// leave the archive half applied.
        /// </summary>
        private static readonly UTF8Encoding Utf8 =
            new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

        private static readonly JsonSerializerOptions ManifestJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            AllowTrailingCommas = true,
            WriteIndented = true
        };

        #region Archive

        /// <summary>
        /// Archive a Project.
        /// Assumes all Directories are fully populated with Files, Workspaces, and child Directories
        /// </summary>
        public async Task<ArchiveResult> ArchiveProject(Project project, ArchiveType type, bool includeIds, bool includeSettings)
        {
            var stream = new System.IO.MemoryStream();

            var manifest = includeSettings ? NewManifest("project", project.Name) : null;

            using (ArchiveOutputStream archiveStream = ArchiveOutputStream.Create(stream, type))
            {
                foreach (var directory in project.Directories.Where(d => d.ParentId == null))
                {
                    await ArchiveDirectory(directory, "", archiveStream, includeIds, includeDirName: true, manifest);
                }

                await WriteManifest(archiveStream, manifest);
            }

            stream.Position = 0;

            return new ArchiveResult
            {
                Data = stream,
                Name = $"{project.Name}.{type.GetExtension()}",
                Type = type.GetContentType()
            };
        }

        /// <summary>
        /// Archive a Directory.
        /// Assumes the Directory is fully populated with Files, Workspaces, and child Directories
        /// </summary>
        public async Task<ArchiveResult> ArchiveDirectory(Directory directory, ArchiveType type, bool includeIds, bool includeSettings)
        {
            var stream = new System.IO.MemoryStream();

            var manifest = includeSettings ? NewManifest("directory", directory.Name) : null;

            using (ArchiveOutputStream archiveStream = ArchiveOutputStream.Create(stream, type))
            {
                await ArchiveDirectory(directory, "", archiveStream, includeIds, includeDirName: false, manifest);

                await WriteManifest(archiveStream, manifest);
            }

            stream.Position = 0;

            return new ArchiveResult
            {
                Data = stream,
                Name = $"{directory.GetExportName(includeIds)}.{type.GetExtension()}",
                Type = type.GetContentType()
            };
        }

        private async Task ArchiveDirectory(Directory directory, string ancestors, ArchiveOutputStream archiveStream, bool includeIds, bool includeDirName, ArchiveManifest manifest)
        {
            // When the Directory's own name is not part of the path, the ancestors are the whole
            // path. Appending a separator anyway produced entry names rooted at "/", which some
            // extractors treat as absolute.
            var name = includeDirName ? directory.GetExportName(includeIds) : string.Empty;
            var rootPath = name.Length == 0 ? ancestors : $"{ancestors}{name}/";

            if (manifest != null)
            {
                manifest.Directories[ToManifestKey(rootPath)] = new ArchiveManifestDirectory
                {
                    TerraformVersion = directory.TerraformVersion,
                    Parallelism = directory.Parallelism,
                    AzureDestroyFailureThresholdEnabled = directory.AzureDestroyFailureThresholdEnabled,
                    AzureDestroyFailureThreshold = directory.AzureDestroyFailureThreshold
                };
            }

            foreach (var file in directory.Files.Where(f => f.WorkspaceId == null))
            {
                await WriteEntry(archiveStream, $"{rootPath}{file.Name}", file.Content);
            }

            foreach (var workspace in directory.Workspaces)
            {
                var path = $"{rootPath}{WorkspacesEntryName}/{workspace.Name}/";

                if (manifest != null)
                {
                    manifest.Workspaces[ToManifestKey(path)] = new ArchiveManifestWorkspace
                    {
                        TerraformVersion = workspace.TerraformVersion,
                        Parallelism = workspace.Parallelism,
                        AzureDestroyFailureThreshold = workspace.AzureDestroyFailureThreshold
                    };
                }

                archiveStream.PutNextEntry(path, 0);
                archiveStream.CloseEntry();

                foreach (var file in workspace.Files)
                {
                    await WriteEntry(archiveStream, $"{path}{file.Name}", file.Content);
                }
            }

            foreach (var dir in directory.Children)
            {
                await ArchiveDirectory(dir, $"{rootPath}", archiveStream, includeIds, true, manifest);
            }
        }

        /// <summary>
        /// Writes text content as a single entry. The declared entry size and the bytes written
        /// come from the same buffer by construction, which tar requires - it stores the size in
        /// the entry header and enforces it, unlike zip.
        /// </summary>
        private static async Task WriteEntry(ArchiveOutputStream archiveStream, string name, string content)
        {
            var bytes = Utf8.GetBytes(content ?? string.Empty);

            archiveStream.PutNextEntry(name, bytes.Length);
            await archiveStream.Stream.WriteAsync(bytes, 0, bytes.Length);
            archiveStream.CloseEntry();
        }

        private static ArchiveManifest NewManifest(string sourceType, string sourceName)
        {
            return new ArchiveManifest
            {
                Source = new ArchiveManifestSource
                {
                    Type = sourceType,
                    Name = sourceName
                }
            };
        }

        private static async Task WriteManifest(ArchiveOutputStream archiveStream, ArchiveManifest manifest)
        {
            if (manifest == null)
            {
                return;
            }

            await WriteEntry(archiveStream, ManifestEntryName, JsonSerializer.Serialize(manifest, ManifestJsonOptions));
        }

        /// <summary>
        /// Normalized the same way entry paths are split on extract, so that the two agree
        /// regardless of leading, trailing, or repeated separators.
        /// </summary>
        private static string ToManifestKey(string path)
        {
            return string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries));
        }

        #endregion

        #region Extract

        public ArchiveExtractResult<Project> ExtractProject(System.IO.Stream stream, string filename)
        {
            Project project = new Project(System.IO.Path.GetFileNameWithoutExtension(filename));
            var result = new ArchiveExtractResult<Project> { Entity = project };

            this.Extract(stream, filename, result, project: project);

            return result;
        }

        public ArchiveExtractResult<Directory> ExtractDirectory(System.IO.Stream stream, string filename)
        {
            Directory directory = new Directory(System.IO.Path.GetFileNameWithoutExtension(filename), null);
            var result = new ArchiveExtractResult<Directory> { Entity = directory };

            this.Extract(stream, filename, result, directory: directory);

            return result;
        }

        private void Extract(System.IO.Stream stream, string filename, ArchiveExtractResult result, Project project = null, Directory directory = null)
        {
            // Entities keyed by their path within the archive, so that manifest settings can be
            // matched back to them once every entry has been read.
            var directoryKeys = new Dictionary<string, Directory>(StringComparer.Ordinal);
            var workspaceKeys = new Dictionary<string, Workspace>(StringComparer.Ordinal);

            if (directory != null)
            {
                directoryKeys[string.Empty] = directory;
            }

            using (var archiveInputStream = ArchiveInputStream.Create(stream, ArchiveTypeHelpers.GetType(filename)))
            {
                while (archiveInputStream.GetNextEntry() is ArchiveEntry archiveEntry)
                {
                    var pathParts = this.SplitPath(archiveEntry.Name ?? string.Empty);

                    if (pathParts.Length > 0 && pathParts[0].Equals(ReservedEntryName, StringComparison.Ordinal))
                    {
                        // Reserved for Caster's own metadata. Never materialized as a File.
                        if (archiveEntry.IsFile &&
                            string.Join('/', pathParts).Equals(ManifestEntryName, StringComparison.Ordinal))
                        {
                            result.Manifest = this.ReadManifest(archiveInputStream, result);
                        }

                        continue;
                    }

                    var file = this.EnsureCreated(project, directory, archiveEntry.Name, archiveEntry.IsFile, directoryKeys, workspaceKeys);

                    if (file != null && archiveEntry.IsFile)
                    {
                        var buffer = new byte[4096];

                        using (var contentStream = new System.IO.MemoryStream())
                        {
                            StreamUtils.Copy(archiveInputStream.Stream, contentStream, buffer);
                            file.Content = Utf8.GetString(contentStream.ToArray());
                        }
                    }
                }
            }

            this.ApplyManifest(result, directoryKeys, workspaceKeys);
        }

        private ArchiveManifest ReadManifest(ArchiveInputStream archiveInputStream, ArchiveExtractResult result)
        {
            try
            {
                using var contentStream = new System.IO.MemoryStream();
                StreamUtils.Copy(archiveInputStream.Stream, contentStream, new byte[4096]);

                return JsonSerializer.Deserialize<ArchiveManifest>(
                    Utf8.GetString(contentStream.ToArray()),
                    ManifestJsonOptions);
            }
            catch (Exception ex) when (ex is JsonException || ex is NotSupportedException)
            {
                // An unreadable manifest must not fail the Import. The archive still carries
                // all of the structure and file content it did before manifests existed.
                result.Warnings.Add("The archive contains a settings manifest that could not be read. Settings were not restored.");
                return null;
            }
        }

        #region Manifest

        private void ApplyManifest(
            ArchiveExtractResult result,
            Dictionary<string, Directory> directoryKeys,
            Dictionary<string, Workspace> workspaceKeys)
        {
            var manifest = result.Manifest;

            if (manifest == null)
            {
                return;
            }

            if (manifest.SchemaVersion > ArchiveManifest.CurrentSchemaVersion)
            {
                result.Warnings.Add($"The archive's settings manifest uses schema version {manifest.SchemaVersion}, which is newer than this system supports. Some settings may not be restored.");
            }

            foreach (var entry in manifest.Directories ?? [])
            {
                if (!directoryKeys.TryGetValue(entry.Key, out var directory))
                {
                    result.SkippedSettings.Add($"Directory '{entry.Key}': not present in the archive's contents.");
                    continue;
                }

                var settings = entry.Value;

                if (settings == null)
                {
                    continue;
                }

                if (TryUseVersion(settings.TerraformVersion, $"Directory '{entry.Key}'", result))
                {
                    directory.TerraformVersion = settings.TerraformVersion;
                }

                if (TryUseParallelism(settings.Parallelism, $"Directory '{entry.Key}'", result))
                {
                    directory.Parallelism = settings.Parallelism;
                }

                if (TryUseAzureThreshold(settings.AzureDestroyFailureThreshold, $"Directory '{entry.Key}'", result))
                {
                    directory.AzureDestroyFailureThreshold = settings.AzureDestroyFailureThreshold;
                }

                if (settings.AzureDestroyFailureThresholdEnabled.HasValue)
                {
                    directory.AzureDestroyFailureThresholdEnabled = settings.AzureDestroyFailureThresholdEnabled.Value;
                }
            }

            foreach (var entry in manifest.Workspaces ?? [])
            {
                if (!workspaceKeys.TryGetValue(entry.Key, out var workspace))
                {
                    result.SkippedSettings.Add($"Workspace '{entry.Key}': not present in the archive's contents.");
                    continue;
                }

                var settings = entry.Value;

                if (settings == null)
                {
                    continue;
                }

                if (TryUseVersion(settings.TerraformVersion, $"Workspace '{entry.Key}'", result))
                {
                    workspace.TerraformVersion = settings.TerraformVersion;
                }

                if (TryUseParallelism(settings.Parallelism, $"Workspace '{entry.Key}'", result))
                {
                    workspace.Parallelism = settings.Parallelism;
                }

                if (TryUseAzureThreshold(settings.AzureDestroyFailureThreshold, $"Workspace '{entry.Key}'", result))
                {
                    workspace.AzureDestroyFailureThreshold = settings.AzureDestroyFailureThreshold;
                }
            }
        }

        /// <summary>
        /// A manifest comes from a user supplied archive, and the version it names is used to
        /// build a path under the Terraform binary directory and a container image reference.
        /// Reject anything that is not a plain version-like token before asking whether the
        /// system actually has it. A version this system does not have is reported and dropped,
        /// leaving the entity unpinned, rather than failing the Import.
        /// </summary>
        private bool TryUseVersion(string version, string label, ArchiveExtractResult result)
        {
            if (string.IsNullOrEmpty(version))
            {
                return false;
            }

            if (!IsSafeVersionToken(version))
            {
                result.SkippedSettings.Add($"{label}: Terraform version is not a valid version and was ignored.");
                return false;
            }

            if (!terraformService.IsValidVersion(version))
            {
                result.SkippedSettings.Add($"{label}: Terraform version '{version}' is not available on this system. The default version will be used.");
                return false;
            }

            return true;
        }

        private static bool IsSafeVersionToken(string version)
        {
            return version.Length <= 64 &&
                version != "." &&
                version != ".." &&
                version.All(c => char.IsAsciiLetterOrDigit(c) || c == '.' || c == '-' || c == '_');
        }

        private bool TryUseParallelism(int? parallelism, string label, ArchiveExtractResult result)
        {
            if (!parallelism.HasValue)
            {
                return false;
            }

            if (parallelism.Value <= 0 || parallelism.Value >= terraformOptions.MaxParallelism)
            {
                result.SkippedSettings.Add($"{label}: parallelism of {parallelism.Value} is outside of the range allowed by this system and was ignored.");
                return false;
            }

            return true;
        }

        private static bool TryUseAzureThreshold(int? threshold, string label, ArchiveExtractResult result)
        {
            if (!threshold.HasValue)
            {
                return false;
            }

            if (threshold.Value <= 0 || threshold.Value > 10)
            {
                result.SkippedSettings.Add($"{label}: Azure destroy failure threshold of {threshold.Value} is outside of the allowed range and was ignored.");
                return false;
            }

            return true;
        }

        #endregion

        private File EnsureCreated(
            Project project,
            Directory root,
            string path,
            bool isFile,
            Dictionary<string, Directory> directoryKeys,
            Dictionary<string, Workspace> workspaceKeys)
        {
            var isWorkspace = false;
            Directory parent = root;
            Workspace workspace = null;
            File file = null;

            if (String.IsNullOrEmpty(path))
            {
                return file;
            }

            string[] pathParts = this.SplitPath(path);
            var entryKey = string.Empty;

            for (int i = 0; i < pathParts.Length; i++)
            {
                var pathPart = pathParts[i];

                // Tracked for every segment, including the __Workspaces__ sentinel, so that it
                // matches the keys written into the manifest on export.
                entryKey = entryKey.Length == 0 ? pathPart : $"{entryKey}/{pathPart}";

                if (pathPart.Equals(WorkspacesEntryName))
                {
                    isWorkspace = true;
                    continue;
                }

                if (isWorkspace)
                {
                    if (isFile &&
                        (i == pathParts.Length - 1) &&
                        workspace != null &&
                        !workspace.Files.Any(x => x.Name.Equals(pathPart)))
                    {
                        file = new File();
                        file.Name = pathPart;
                        workspace.Files.Add(file);
                    }
                    else
                    {
                        var newWorkspace = new Workspace(pathPart, parent);
                        var existingWorkspace = parent.Workspaces.FirstOrDefault(x => x.Name.Equals(pathPart));

                        if (existingWorkspace == null)
                        {
                            parent.Workspaces.Add(newWorkspace);
                            workspace = newWorkspace;
                        }
                        else
                        {
                            workspace = existingWorkspace;
                        }

                        workspaceKeys[entryKey] = workspace;
                    }
                }
                else
                {
                    if (isFile &&
                        (i == pathParts.Length - 1) &&
                        !parent.Files.Any(x => x.Name.Equals(pathPart)))
                    {
                        file = new File();
                        file.Name = pathPart;
                        parent.Files.Add(file);
                    }
                    else
                    {
                        var newDir = new Directory(pathPart, parent);
                        Directory existingDir = null;

                        if (parent == null && project != null)
                        {
                            existingDir = project.Directories.FirstOrDefault(x => x.Name == newDir.Name);
                        }
                        else if (parent != null)
                        {
                            existingDir = parent.Children.FirstOrDefault(x => x.Name == newDir.Name);
                        }

                        if (existingDir == null)
                        {
                            if (parent == null)
                            {
                                project.Directories.Add(newDir);
                            }
                            else
                            {
                                parent.Children.Add(newDir);
                            }

                            parent = newDir;
                        }
                        else
                        {
                            parent = existingDir;
                        }

                        directoryKeys[entryKey] = parent;
                    }
                }
            }

            return file;
        }

        private string[] SplitPath(string path)
        {
            var paths = System.IO.Path.TrimEndingDirectorySeparator(path).Split(new[]
            {
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar
            });

            return paths.Where(x => x != string.Empty).ToArray();
        }

        #endregion

        private class ArchiveOutputStream : IDisposable
        {
            public System.IO.Stream Stream { get; set; }

            private ArchiveOutputStream(System.IO.Stream stream)
            {
                this.Stream = stream;
            }

            public static ArchiveOutputStream Create(System.IO.Stream stream, ArchiveType type)
            {
                switch (type)
                {
                    case ArchiveType.zip:
                        return ArchiveOutputStream.CreateZipStream(stream);
                    case ArchiveType.tgz:
                        return ArchiveOutputStream.CreateTgzStream(stream);
                    default:
                        throw new ArgumentException();
                }
            }

            private static ArchiveOutputStream CreateZipStream(System.IO.Stream stream)
            {
                ZipOutputStream zipStream = new ZipOutputStream(stream);
                zipStream.IsStreamOwner = false;
                return new ArchiveOutputStream(zipStream);
            }

            private static ArchiveOutputStream CreateTgzStream(System.IO.Stream stream)
            {
                GZipOutputStream gzipStream = new GZipOutputStream(stream);
                // Without an explicit encoding, non-ASCII bytes in entry names are discarded
                TarOutputStream tarStream = new TarOutputStream(gzipStream, Utf8);
                gzipStream.IsStreamOwner = false;
                return new ArchiveOutputStream(tarStream);
            }

            public void PutNextEntry(string name, long size)
            {
                if (Stream is ZipOutputStream)
                {
                    // Flags the name as UTF-8 so that non-ASCII names are not read back
                    // through whatever the extracting system's default code page happens to be
                    ((ZipOutputStream)Stream).PutNextEntry(new ZipEntry(name) { IsUnicodeText = true });
                }
                else if (Stream is TarOutputStream)
                {
                    var tarEntry = TarEntry.CreateTarEntry(name);
                    tarEntry.Size = size;
                    ((TarOutputStream)Stream).PutNextEntry(tarEntry);
                }
            }

            public void CloseEntry()
            {
                if (Stream is TarOutputStream)
                {
                    ((TarOutputStream)Stream).CloseEntry();
                }
            }

            public void Dispose()
            {
                this.Stream.Dispose();
            }
        }

        private class ArchiveInputStream : IDisposable
        {
            public System.IO.Stream Stream { get; set; }

            private ArchiveInputStream(System.IO.Stream stream)
            {
                this.Stream = stream;
            }

            public static ArchiveInputStream Create(System.IO.Stream stream, ArchiveType type)
            {
                switch (type)
                {
                    case ArchiveType.zip:
                        return ArchiveInputStream.CreateZipStream(stream);
                    case ArchiveType.tgz:
                        return ArchiveInputStream.CreateTgzStream(stream);
                    default:
                        throw new ArgumentException();
                }
            }

            private static ArchiveInputStream CreateZipStream(System.IO.Stream stream)
            {
                ZipInputStream zipStream = new ZipInputStream(stream);
                zipStream.IsStreamOwner = false;
                return new ArchiveInputStream(zipStream);
            }

            private static ArchiveInputStream CreateTgzStream(System.IO.Stream stream)
            {
                GZipInputStream gzipStream = new GZipInputStream(stream);
                // Entry names are UTF-8. ASCII names written before this decode identically.
                TarInputStream tarStream = new TarInputStream(gzipStream, Utf8);
                gzipStream.IsStreamOwner = false;
                return new ArchiveInputStream(tarStream);
            }

            public ArchiveEntry GetNextEntry()
            {
                ArchiveEntry archiveEntry = null;

                if (Stream is ZipInputStream)
                {
                    var zipEntry = ((ZipInputStream)Stream).GetNextEntry();

                    if (zipEntry != null)
                    {
                        archiveEntry = new ArchiveEntry(zipEntry);
                    }
                }
                else if (Stream is TarInputStream)
                {
                    var tarEntry = ((TarInputStream)Stream).GetNextEntry();

                    if (tarEntry != null)
                    {
                        archiveEntry = new ArchiveEntry(tarEntry);
                    }
                }

                return archiveEntry;
            }

            public void Dispose()
            {
                this.Stream.Dispose();
            }
        }

        private class ArchiveEntry
        {
            private ZipEntry ZipEntry{ get; set; }
            private TarEntry TarEntry { get; set; }

            public ArchiveEntry(ZipEntry zipEntry)
            {
                this.ZipEntry = zipEntry;
            }

            public ArchiveEntry(TarEntry tarEntry)
            {
                this.TarEntry = tarEntry;
            }

            public bool IsFile
            {
                get
                {
                    if (this.ZipEntry != null)
                    {
                        return !this.ZipEntry.IsDirectory;
                    }

                    if (this.TarEntry != null)
                    {
                        return !this.TarEntry.IsDirectory;
                    }

                    return false;
                }
            }

            public string Name
            {
                get
                {
                    if (this.ZipEntry != null)
                    {
                        return this.ZipEntry.Name;
                    }

                    if (this.TarEntry != null)
                    {
                        return this.TarEntry.Name;
                    }

                    return null;
                }
            }
        }
    }
}
