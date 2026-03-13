// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Security.Claims;
using Caster.Api.Infrastructure.Extensions;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace Caster.Api.Tests.Unit.Infrastructure
{
    [Category("Unit")]
    [Category("ClaimsPrincipal")]
    public class ClaimsPrincipalExtensionsTests
    {
        [Test]
        public async Task GetId_WithSubClaim_ReturnsGuid()
        {
            var userId = Guid.NewGuid();
            var claims = new[] { new Claim("sub", userId.ToString()) };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

            var result = principal.GetId();

            await Assert.That(result).IsEqualTo(userId);
        }

        [Test]
        public async Task GetId_WithNameIdentifierClaim_ReturnsGuid()
        {
            var userId = Guid.NewGuid();
            var claims = new[] { new Claim("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier", userId.ToString()) };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

            var result = principal.GetId();

            await Assert.That(result).IsEqualTo(userId);
        }

        [Test]
        public async Task GetId_WithNullPrincipal_ReturnsEmptyGuid()
        {
            ClaimsPrincipal? principal = null;

            var result = principal.GetId();

            await Assert.That(result).IsEqualTo(Guid.Empty);
        }

        [Test]
        public async Task NormalizeScopeClaims_WithSpaceSeparatedScopes_SplitsThem()
        {
            var claims = new[] { new Claim("scope", "openid profile email") };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

            var normalized = principal.NormalizeScopeClaims();

            var scopeClaims = normalized.FindAll("scope").ToList();
            await Assert.That(scopeClaims.Count).IsEqualTo(3);
        }

        [Test]
        public async Task NormalizeScopeClaims_WithSingleScope_KeepsAsIs()
        {
            var claims = new[] { new Claim("scope", "openid") };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

            var normalized = principal.NormalizeScopeClaims();

            var scopeClaims = normalized.FindAll("scope").ToList();
            await Assert.That(scopeClaims).HasSingleItem();
            await Assert.That(scopeClaims[0].Value).IsEqualTo("openid");
        }

        [Test]
        public async Task NormalizeScopeClaims_WithNonScopeClaims_PreservesThem()
        {
            var userId = Guid.NewGuid();
            var claims = new[]
            {
                new Claim("sub", userId.ToString()),
                new Claim("scope", "openid profile")
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

            var normalized = principal.NormalizeScopeClaims();

            await Assert.That(normalized.FindFirst("sub")?.Value).IsEqualTo(userId.ToString());
        }
    }
}
