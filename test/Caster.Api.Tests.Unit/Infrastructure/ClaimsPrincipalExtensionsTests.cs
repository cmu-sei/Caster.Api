// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Security.Claims;
using Caster.Api.Infrastructure.Extensions;
using Xunit;

namespace Caster.Api.Tests.Unit.Infrastructure
{
    [Trait("Category", "Unit")]
    public class ClaimsPrincipalExtensionsTests
    {
        [Fact]
        public void GetId_WithSubClaim_ReturnsGuid()
        {
            var userId = Guid.NewGuid();
            var claims = new[] { new Claim("sub", userId.ToString()) };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

            var result = principal.GetId();

            Assert.Equal(userId, result);
        }

        [Fact]
        public void GetId_WithNameIdentifierClaim_ReturnsGuid()
        {
            var userId = Guid.NewGuid();
            var claims = new[] { new Claim("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier", userId.ToString()) };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

            var result = principal.GetId();

            Assert.Equal(userId, result);
        }

        [Fact]
        public void GetId_WhenPrincipalIsNull_ReturnsEmptyGuid()
        {
            ClaimsPrincipal principal = null;

            var result = principal.GetId();

            Assert.Equal(Guid.Empty, result);
        }

        [Fact]
        public void NormalizeScopeClaims_WithSpaceSeparatedScopes_SplitsIntoMultipleClaims()
        {
            var claims = new[] { new Claim("scope", "openid profile email") };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

            var normalized = principal.NormalizeScopeClaims();

            var scopeClaims = normalized.FindAll("scope").ToList();
            Assert.Equal(3, scopeClaims.Count);
        }

        [Fact]
        public void NormalizeScopeClaims_WithSingleScope_KeepsAsIs()
        {
            var claims = new[] { new Claim("scope", "openid") };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

            var normalized = principal.NormalizeScopeClaims();

            var scopeClaims = normalized.FindAll("scope").ToList();
            Assert.Single(scopeClaims);
            Assert.Equal("openid", scopeClaims[0].Value);
        }

        [Fact]
        public void NormalizeScopeClaims_WithNonScopeClaims_PreservesThem()
        {
            var userId = Guid.NewGuid();
            var claims = new[]
            {
                new Claim("sub", userId.ToString()),
                new Claim("scope", "openid profile")
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

            var normalized = principal.NormalizeScopeClaims();

            Assert.Equal(userId.ToString(), normalized.FindFirst("sub")?.Value);
        }
    }
}
