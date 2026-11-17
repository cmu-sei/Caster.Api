// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;

namespace Caster.Api.Features.Directories
{
    public static class DirectoryExtensions
    {
        /// <summary>
        /// Projects the directories with their files, workspaces and designs when <paramref name="includeRelated"/>
        /// is set, and the files' content as well when <paramref name="includeFileContent"/> is; without
        /// <paramref name="includeRelated"/>, <paramref name="includeFileContent"/> has no effect.
        /// </summary>
        public static IQueryable<Directory> Expand(this IQueryable<Domain.Models.Directory> query, bool includeRelated, bool includeFileContent)
        {
            if (!includeRelated)
                return query.ProjectToDirectory();

            return includeFileContent
                ? query.ProjectToDirectoryWithRelatedAndFileContent()
                : query.ProjectToDirectoryWithRelated();
        }
    }
}
