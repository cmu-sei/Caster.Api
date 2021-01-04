// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using System.Threading.Tasks;
using Caster.Api.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Caster.Api.Infrastructure.Identity
{
    public interface IIdentityResolver
    {
        ClaimsPrincipal GetClaimsPrincipal();
        Task<bool> IsAdminAsync();
    }

    public class IdentityResolver: IIdentityResolver
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IAuthorizationService _authorizationService;

        public IdentityResolver(
            IHttpContextAccessor httpContextAccessor,
            IAuthorizationService authorizationService)
        {
            _httpContextAccessor = httpContextAccessor;
            _authorizationService = authorizationService;
        }

        public ClaimsPrincipal GetClaimsPrincipal()
        {
            return _httpContextAccessor?.HttpContext?.User as ClaimsPrincipal;
        }

        public async Task<bool> IsAdminAsync()
        {
            if ((await _authorizationService.AuthorizeAsync(
                this.GetClaimsPrincipal(),
                null,
                new FullRightsRequirement())).Succeeded)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
