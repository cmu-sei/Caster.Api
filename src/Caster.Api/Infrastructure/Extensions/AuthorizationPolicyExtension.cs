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
            });

            services.AddSingleton<IAuthorizationHandler, SystemPermissionsHandler>();
            services.AddSingleton<IAuthorizationHandler, ProjectPermissionsHandler>();
        }
    }
}
