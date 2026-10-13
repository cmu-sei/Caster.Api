// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Data
{
    public partial class CasterContext
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

            var contentBuilder = new StringBuilder();

            foreach (var design in designs)
            {
                foreach (var variable in design.Variables)
                {
                    contentBuilder.Append($"{variable.ToSnippet()}\n");
                }

                foreach (var designModule in design.Modules.Where(x => x.Enabled))
                {
                    var moduleVersion = await this.ModuleVersions
                        .Where(x => x.Name == designModule.ModuleVersion && x.ModuleId == designModule.ModuleId)
                        .FirstOrDefaultAsync();

                    if (moduleVersion == null && !string.IsNullOrEmpty(designModule.ModuleVersion))
                    {
                        throw new ConflictException(
                            $"Module {designModule.Name} in Design {design.Name} uses version {designModule.ModuleVersion}, which no longer exists");
                    }

                    // Design modules added before a version was saved on create have none.
                    // Use the highest, which is also what the Designer shows them as.
                    if (moduleVersion == null)
                    {
                        var versions = await this.ModuleVersions
                            .Where(x => x.ModuleId == designModule.ModuleId)
                            .ToListAsync();

                        moduleVersion = ModuleVersionOrder.HighestFirst(versions).FirstOrDefault()
                            ?? throw new ConflictException(
                                $"Module {designModule.Name} in Design {design.Name} has no versions to use");
                    }

                    contentBuilder.Append($"{moduleVersion.ToSnippet(designModule.Name, designModule.Values)}\n");
                }

                files.Add(new File()
                {
                    Name = $"{design.Name}-design.tf",
                    Content = contentBuilder.ToString()
                });

                contentBuilder.Clear();
            }

            return files;
        }

        public async Task<bool> AnyIncompleteRuns(Guid workspaceId)
        {
            return await this.Runs
               .Where(r =>
                   r.WorkspaceId == workspaceId &&
                   !RunHelpers.TerminalStatuses.Contains(r.Status))
               .AnyAsync();
        }
    }
}
