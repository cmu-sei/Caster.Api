// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;

namespace Caster.Api.Features.Vlan
{
    public class Partition
    {
        public Guid Id { get; set; }
        public Guid PoolId { get; set; }
        public string Name { get; set; }
        public bool IsDefault { get; set; }
    }
}

