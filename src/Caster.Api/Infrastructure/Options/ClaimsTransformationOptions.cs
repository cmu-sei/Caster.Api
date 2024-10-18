// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Caster.Api.Infrastructure.Options
{
    public class ClaimsTransformationOptions
    {
        public bool EnableCaching { get; set; }
        public double CacheExpirationSeconds { get; set; }
        public bool UseRolesFromIdP { get; set; }
        public string RolesClaimPath { get; set; }
        public bool UseGroupsFromIdP { get; set; }
        public string GroupsClaimPath { get; set; }
    }
}

