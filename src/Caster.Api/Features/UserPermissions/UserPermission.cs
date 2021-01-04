// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;

namespace Caster.Api.Features.UserPermissions
{
    public class UserPermission
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid PermissionId { get; set; }
    }
}

