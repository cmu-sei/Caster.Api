// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;

namespace Caster.Api.Features.SystemRoles
{
    public class SystemRole
    {

        public Guid Id { get; set; }

        public string Name { get; set; }

        public bool AllPermissions { get; set; }
        public bool Immutable { get; set; }
        public Domain.Models.SystemPermission[] Permissions { get; set; }
    }
}

