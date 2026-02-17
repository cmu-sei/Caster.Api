// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Data
{
    public partial class CasterContext
    {
        public async Task<Pool> CreateVlanPool(Pool pool, int[] reservedVlanIds, bool reservedEditable, CancellationToken cancellationToken)
        {
            Pools.Add(pool);
            await SaveChangesAsync(cancellationToken);

            var vlans = new List<Vlan>();

            for (int i = 0; i < 4096; i++)
            {
                var reserved = reservedVlanIds.Contains(i);
                vlans.Add(new Vlan
                {
                    VlanId = i,
                    Reserved = reserved,
                    ReservedEditable = reserved ? reservedEditable : true,
                    PoolId = pool.Id
                });
            }

            await this.BulkInsertAsync(vlans,
                new BulkConfig { PropertiesToExclude = new List<string> { nameof(Pool.Id) } }); // workaround until id properties in pgsql are fixed

            return pool;
        }
    }
}
