// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// Wired as AuthorizationPolicyExtensions.AddAuthorizationPolicy wires it: the framework authorization
// service and Caster's three requirement handlers. CasterAuthorizationService builds Caster's own
// ICasterAuthorizationService over it for a principal a test chose.

using System.Security.Claims;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Caster.Api.Tests.Support;

/// <summary>The authorization stack wired as production wires it, for testing handlers directly.</summary>
public static class AuthorizationHarness
{
    /// <summary>The framework authorization service with Caster's handlers registered.</summary>
    public static IAuthorizationService CreateFrameworkAuthorizationService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationHandler, SystemPermissionsHandler>();
        services.AddSingleton<IAuthorizationHandler, ProjectPermissionsHandler>();
        services.AddSingleton<IAuthorizationHandler, GroupPermissionsHandler>();

        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    /// <summary>
    /// Caster's real <see cref="AuthorizationService"/> over <paramref name="db"/>, deciding for
    /// <paramref name="user"/>. The identity resolver is a substitute the caller owns: it only answers who
    /// is asking, which a request would have answered from its <c>HttpContext</c>.
    /// </summary>
    public static ICasterAuthorizationService CreateCasterAuthorizationService(CasterContext db, ClaimsPrincipal user)
    {
        var identity = Substitute.For<IIdentityResolver>();
        identity.GetClaimsPrincipal().Returns(user);

        return new AuthorizationService(CreateFrameworkAuthorizationService(), identity, db);
    }

    /// <summary>Runs a requirement through a handler directly and returns the resulting context.</summary>
    public static async Task<AuthorizationHandlerContext> HandleAsync<TRequirement>(
        IAuthorizationHandler handler,
        TRequirement requirement,
        ClaimsPrincipal user,
        object resource = null)
        where TRequirement : IAuthorizationRequirement
    {
        var context = new AuthorizationHandlerContext([requirement], user, resource);
        await handler.HandleAsync(context);

        return context;
    }
}
