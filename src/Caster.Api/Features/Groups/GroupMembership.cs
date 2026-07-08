// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Groups
{
    public class GroupMembership
    {
        public Guid Id { get; set; }

        /// <summary>
        /// ID of the group.
        /// </summary>
        public Guid GroupId { get; set; }

        /// <summary>
        /// Id of the User.
        /// </summary>
        public Guid UserId { get; set; }

        /// <summary>
        /// The User's role within the Group.
        /// </summary>
        public GroupMembershipRole Role { get; set; }
    }
}
