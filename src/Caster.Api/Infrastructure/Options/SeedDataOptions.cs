// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Domain.Models;
using System.Collections.Generic;

namespace Caster.Api.Infrastructure.Options
{
    public class SeedDataOptions
    {
        public List<SystemRole> Roles { get; set; }
        public List<User> Users { get; set; }
        public List<Group> Groups { get; set; }
    }
}

