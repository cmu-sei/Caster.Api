// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Caster.Api.Data
{
    public partial class CasterContext : DbContext
    {
        /// <summary>
        /// Returns an array of EntityEntry for each entity that has been Added or Modified
        /// </summary>
        public EntityEntry[] GetUpdatedEntries()
        {
            var entries = this.ChangeTracker.Entries()
                .Where(x => x.State == EntityState.Added ||
                            x.State == EntityState.Modified)
                .ToArray();

            return entries;
        }
    }
}
