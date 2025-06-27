// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Caster.Api.Infrastructure.Options
{
    public class SeedDataOptions
    {
        public List<SystemRole> Roles { get; set; }
        public List<User> Users { get; set; }
        public List<Group> Groups { get; set; }
        public SeedVlans Vlans { get; set; }
    }

    public class SeedVlans
    {
        public SeedVlanPool[] Pools { get; set; } = [];
        public List<string> Validate(IEnumerable<Pool> existingPools)
        {
            var errors = new List<string>();

            var numDefaultPartitions = Pools.SelectMany(x => x.Partitions).Where(x => x.IsDefault).Count();
            var defaultExistingPool = existingPools.Any(x => x.IsDefault);

            if (numDefaultPartitions > 1)
            {
                errors.Add("Only one Partition can be set as the default");
            }
            else if (defaultExistingPool && numDefaultPartitions == 1)
            {
                errors.Add("Cannot add a default Partition when one already exists in the database");
            }

            foreach (var pool in Pools)
            {
                errors.AddRange(pool.Validate());
            }

            return errors;
        }
    }

    public class SeedVlanPool
    {
        public string Name { get; set; }
        public string[] ReservedIds { get; set; } = [];
        public int[] Reserved => IdParser.ParseIds(ReservedIds);
        public SeedVlanPartition[] Partitions { get; set; } = [];
        public List<string> Validate()
        {
            var errors = new List<string>();

            var reservedSet = new HashSet<int>(Reserved);
            var seenVlans = new HashSet<int>();

            foreach (var partition in Partitions ?? Array.Empty<SeedVlanPartition>())
            {
                var vlanSet = new HashSet<int>(partition.Vlans);

                var overlapWithReserved = vlanSet.Intersect(reservedSet).ToArray();
                if (overlapWithReserved.Length > 0)
                {
                    errors.Add($"{Name}: Partition '{partition.Name}' has VLANs overlapping with Reserved: {string.Join(", ", overlapWithReserved)}");
                }

                var overlapWithSeen = vlanSet.Intersect(seenVlans).ToArray();
                if (overlapWithSeen.Length > 0)
                {
                    errors.Add($"{Name}: Partition '{partition.Name}' has VLANs overlapping with other partitions: {string.Join(", ", overlapWithSeen)}");
                }

                seenVlans.UnionWith(vlanSet);
            }

            return errors;
        }
    }

    public class SeedVlanPartition
    {
        public string Name { get; set; }
        public bool IsDefault { get; set; }
        public string[] VlanIds { get; set; } = [];
        public int[] Vlans => IdParser.ParseIds(VlanIds);
    }
}

