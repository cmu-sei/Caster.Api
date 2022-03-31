// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;

namespace Caster.Api.Features.Vlan
{
    public class Vlan
    {

        public Guid Id { get; set; }
        public Guid PoolId { get; set; }
        public Guid PartitionId { get; set; }
        public int vlan { get; set; }
        public bool InUse { get; set; }
    }
}

