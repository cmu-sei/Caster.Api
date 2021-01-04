// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Features.Permissions;

namespace Caster.Api.Features.Users
{
    public class User
    {

        public Guid Id { get; set; }

        public string Name { get; set; }
        public Permission[] Permissions { get; set; }
    }
}

