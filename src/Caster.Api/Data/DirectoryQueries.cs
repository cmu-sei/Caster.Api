// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data.Extensions;
using Caster.Api.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Data
{
    public partial class CasterContext : DbContext
    {
        public async Task<Directory[]> GetDirectoryWithChildren(Guid directoryId, CancellationToken ct)
        {
            var directory = await this.Directories.FindAsync(new object[] { directoryId }, ct);

            if (directory == null)
                return null;

            var directories = await this.Directories
                .GetChildren(directory, includeSelf: true)
                .Include(d => d.Workspaces)
                .Include(d => d.Files)
                .ToArrayAsync();

            return directories;
        }

        public async Task<Directory> GetDirectoryWithAncestors(Guid id, CancellationToken ct)
        {
            var directory = await this.Directories.FindAsync(new object[] { id }, ct);

            if (directory == null)
                return null;

            var ancestorIds = directory.PathIds();

            var directories = await this.Directories
                .Where(x => ancestorIds.Contains(x.Id))
                .ToArrayAsync();

            return directory;
        }
    }
}
