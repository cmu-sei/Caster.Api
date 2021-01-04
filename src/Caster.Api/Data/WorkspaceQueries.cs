// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Data
{
    public partial class CasterContext : DbContext
    {
        public async Task<List<File>> GetWorkspaceFiles(Workspace workspace, Directory directory)
        {
            if (workspace.DirectoryId != directory.Id)
                throw new ArgumentException("workspace must be in directory");

            var pathDirectoryIds = directory.PathIds();

            var files = await this.Files
                .Where(f => (f.WorkspaceId == workspace.Id) ||
                            (pathDirectoryIds.Contains(f.DirectoryId) && f.Workspace == null))
                .ToListAsync();

            return files;
        }

        public async Task<bool> AnyIncompleteRuns(Guid workspaceId)
        {
             return await this.Runs
                .Where(r =>
                    r.WorkspaceId == workspaceId &&
                    r.Status != RunStatus.Applied && r.Status != RunStatus.Failed && r.Status != RunStatus.Rejected)
                .AnyAsync();
        }
    }
}
