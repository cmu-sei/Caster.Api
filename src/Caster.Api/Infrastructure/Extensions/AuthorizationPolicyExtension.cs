// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using Caster.Api.Infrastructure.Authorization;
using System;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Domain.Models;

namespace Caster.Api.Infrastructure.Extensions
{
    public static class AuthorizationPolicyExtensions
    {
        public static void AddAuthorizationPolicy(this IServiceCollection services, Options.AuthorizationOptions authOptions)
        {
            services.AddAuthorization(options =>
            {
                // Require all scopes in authOptions
                var policyBuilder = new AuthorizationPolicyBuilder().RequireAuthenticatedUser();
                Array.ForEach(authOptions.AuthorizationScope.Split(' '), x => policyBuilder.RequireClaim("scope", x));

                options.DefaultPolicy = policyBuilder.Build();

                options.AddPolicy(nameof(CasterClaimTypes.SystemAdmin), policy => policy.Requirements.Add(new FullRightsRequirement()));
                options.AddPolicy(nameof(CasterClaimTypes.ContentDeveloper), policy => policy.Requirements.Add(new ContentDeveloperRequirement()));
                options.AddPolicy(nameof(CasterClaimTypes.BaseUser), policy => policy.Requirements.Add(new BaseUserRequirement()));
                options.AddPolicy(nameof(CasterClaimTypes.Operator), policy => policy.Requirements.Add(new OperatorRequirement()));
                //options.AddPolicy("System Permissions", policy => policy.Requirements.Add(new PermissionsRequirement()));
            });

            services.AddSingleton<IAuthorizationHandler, FullRightsHandler>();
            services.AddSingleton<IAuthorizationHandler, ContentDeveloperHandler>();
            services.AddSingleton<IAuthorizationHandler, OperatorHandler>();
            services.AddSingleton<IAuthorizationHandler, BaseUserHandler>();
            services.AddSingleton<IAuthorizationHandler, SystemPermissionsHandler>();
            services.AddSingleton<IAuthorizationHandler, ProjectPermissionsHandler>();
        }


    }
}
