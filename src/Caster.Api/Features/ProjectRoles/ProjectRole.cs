// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;

namespace Caster.Api.Features.ProjectRoles
{
    public class ProjectRole
    {

        public Guid Id { get; set; }

        public string Name { get; set; }

        public Domain.Models.ProjectPermissions[] Permissions { get; set; }
    }
}

