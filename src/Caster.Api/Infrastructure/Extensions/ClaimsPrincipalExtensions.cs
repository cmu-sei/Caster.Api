// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.


using System;
using System.Security.Claims;

namespace Caster.Api.Infrastructure.Extensions
{
    public static class ClaimsPrincipalExtensions
    {
        public static Guid GetId(this ClaimsPrincipal principal)
        {
            if (principal == null)
            {
                return Guid.Empty;
            }

            try
            {
                return Guid.Parse(principal.FindFirst("sub")?.Value);
            }
            catch
            {
                return Guid.Parse(principal.FindFirst(@"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value);
            }
        }
    }
}
