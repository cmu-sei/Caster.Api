// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Services.Modules;

namespace Caster.Api.Tests.Support
{
    /// <summary>
    /// Stands in for the git binary. "Cloning" copies a prepared directory
    /// tree into the destination the provider chose, so the provider's temp
    /// directory handling and cleanup are exercised for real while nothing
    /// shells out or touches a network.
    /// </summary>
    public class FakeGitCommandRunner : IGitCommandRunner
    {
        /// <summary>
        /// Raw ls-remote tag lines to answer with. HEAD and a main branch are
        /// added when <see cref="DefaultBranchTree"/> or <see cref="HeadSha"/>
        /// is set.
        /// </summary>
        public List<string> LsRemoteLines { get; } = [];

        /// <summary>Directory tree copied into the clone destination.</summary>
        public string SourceTree { get; set; }

        /// <summary>Per-tag trees that override <see cref="SourceTree"/>, for repos whose content changes between tags.</summary>
        public Dictionary<string, string> SourceTreesByTag { get; } = [];

        /// <summary>Tags cloned, in the order the provider asked for them.</summary>
        public List<string> ClonedTags { get; } = [];

        /// <summary>When set, CloneTagAsync throws this instead of copying.</summary>
        public Exception CloneFailure { get; set; }

        /// <summary>Clone destinations handed out, so a test can assert they were deleted.</summary>
        public List<string> CloneDestinations { get; } = [];

        /// <summary>How long each clone takes, so concurrent syncs overlap the way real clones do.</summary>
        public TimeSpan CloneDelay { get; set; }

        /// <summary>
        /// Tree the default branch clones to. Null is a repository with no
        /// commits, which git clones to nothing but a .git directory.
        /// </summary>
        public string DefaultBranchTree { get; set; }

        /// <summary>The sha ls-remote reports for HEAD; defaults to a fixed one when <see cref="DefaultBranchTree"/> is set.</summary>
        public string HeadSha { get; set; }

        /// <summary>HEAD names a branch that does not exist: ls-remote reports no HEAD, but branches exist.</summary>
        public bool DanglingHead { get; set; }

        /// <summary>Default-branch clone destinations handed out, so a test can assert they were deleted.</summary>
        public List<string> DefaultBranchCloneDestinations { get; } = [];

        public int LsRemoteCallCount { get; private set; }
        public int CloneCallCount { get; private set; }

        public DateTime? HeadCommitDate { get; set; } = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        public Task<IReadOnlyList<string>> ListRemoteRefsAsync(string url, CancellationToken cancellationToken)
        {
            LsRemoteCallCount++;
            var lines = new List<string>(LsRemoteLines);

            if (DefaultBranchTree != null || HeadSha != null || DanglingHead)
            {
                var sha = HeadSha ?? "2222222222222222222222222222222222222222";

                // A dangling HEAD gets no line, as from real git.
                if (!DanglingHead)
                {
                    lines.Add($"{sha}\tHEAD");
                }
            }

            return Task.FromResult<IReadOnlyList<string>>(lines.ToArray());
        }

        public int HasBranchesCallCount { get; private set; }

        public Task<bool> HasBranchesAsync(string url, CancellationToken cancellationToken)
        {
            HasBranchesCallCount++;
            return Task.FromResult(DefaultBranchTree != null || HeadSha != null || DanglingHead);
        }

        public async Task CloneTagAsync(string url, string tag, string destinationPath, CancellationToken cancellationToken)
        {
            if (CloneDelay > TimeSpan.Zero)
            {
                await Task.Delay(CloneDelay, cancellationToken);
            }

            CloneCallCount++;
            CloneDestinations.Add(destinationPath);
            ClonedTags.Add(tag);

            if (CloneFailure != null)
            {
                // Mimic a clone that created the destination before failing,
                // which is the case the cleanup path has to cover.
                Directory.CreateDirectory(destinationPath);
                File.WriteAllText(Path.Combine(destinationPath, "partial"), "half a clone");

                throw CloneFailure;
            }

            CopyTree(SourceTreesByTag.GetValueOrDefault(tag, SourceTree), destinationPath);

            // A real clone leaves a .git directory behind; include one so the
            // provider is seen to skip it when enumerating module roots.
            Directory.CreateDirectory(Path.Combine(destinationPath, ".git"));
            File.WriteAllText(Path.Combine(destinationPath, ".git", "HEAD"), "ref: refs/heads/main\n");
        }

        public Task CloneDefaultBranchAsync(string url, string destinationPath, CancellationToken cancellationToken)
        {
            DefaultBranchCloneDestinations.Add(destinationPath);

            if (CloneFailure != null)
            {
                throw CloneFailure;
            }

            if (DefaultBranchTree == null)
            {
                Directory.CreateDirectory(destinationPath);
            }
            else
            {
                CopyTree(DefaultBranchTree, destinationPath);
            }

            Directory.CreateDirectory(Path.Combine(destinationPath, ".git"));
            File.WriteAllText(Path.Combine(destinationPath, ".git", "HEAD"), "ref: refs/heads/main\n");

            return Task.CompletedTask;
        }

        public Task<DateTime?> GetHeadCommitDateAsync(string repositoryPath, CancellationToken cancellationToken) =>
            Task.FromResult(HeadCommitDate);

        private static void CopyTree(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(directory.Replace(source, destination));
            }

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                File.Copy(file, file.Replace(source, destination), overwrite: true);
            }
        }
    }
}
