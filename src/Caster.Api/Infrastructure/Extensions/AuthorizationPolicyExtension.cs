// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Caster.Api.Infrastructure.Authorization;

namespace Caster.Api.Infrastructure.Extensions
{
    public static class AuthorizationPolicyExtensions
    {
        public static void AddAuthorizationPolicy(this IServiceCollection services)
        {
            services.AddAuthorization(options =>
            {
                options.AddPolicy(nameof(CasterClaimTypes.SystemAdmin), policy => policy.Requirements.Add(new FullRightsRequirement()));
                options.AddPolicy(nameof(CasterClaimTypes.ContentDeveloper), policy => policy.Requirements.Add(new ContentDeveloperRequirement()));
                options.AddPolicy(nameof(CasterClaimTypes.BaseUser), policy => policy.Requirements.Add(new BaseUserRequirement()));
                options.AddPolicy(nameof(CasterClaimTypes.Operator), policy => policy.Requirements.Add(new OperatorRequirement()));
            });
            services.AddSingleton<IAuthorizationHandler, FullRightsHandler>();
            services.AddSingleton<IAuthorizationHandler, ContentDeveloperHandler>();
            services.AddSingleton<IAuthorizationHandler, OperatorHandler>();
            services.AddSingleton<IAuthorizationHandler, BaseUserHandler>();
        }


    }
}
