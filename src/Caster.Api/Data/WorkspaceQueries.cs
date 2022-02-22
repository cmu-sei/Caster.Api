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

            var designs = await this.Designs
                .Include(x => x.Modules)
                    .ThenInclude(x => x.Module)
                .Include(x => x.Variables)
                .Where(x => x.Enabled && pathDirectoryIds.Contains(x.DirectoryId))
                .ToListAsync();

            foreach (var design in designs)
            {
                var content = string.Empty;

                foreach (var variable in design.Variables)
                {
                    content += $"{variable.ToSnippet()}\n";
                }

                foreach (var designModule in design.Modules.Where(x => x.Enabled))
                {
                    var moduleVersion = await this.ModuleVersions
                        .Where(x => x.Name == designModule.ModuleVersion && x.ModuleId == designModule.ModuleId)
                        .FirstOrDefaultAsync();

                    content += $"{moduleVersion.ToSnippet(designModule.Name, designModule.Values)}\n";
                }

                files.Add(new File()
                {
                    Name = $"{design.Name}-design.tf",
                    Content = content
                });
            }

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
