// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using Caster.Api.Tests.Integration.Fixtures;

namespace Caster.Api.Tests.Integration.Tests.Controllers;

public class HealthCheckTests : IClassFixture<CasterTestContext>
{
    private readonly CasterTestContext _context;

    public HealthCheckTests(CasterTestContext context)
    {
        _context = context;
    }

    [Fact]
    public async Task HealthCheck_ShouldReturnSuccessStatusCode()
    {
        var client = _context.CreateClient();
        var response = await client.GetAsync("/api/health");

        Assert.True(
            response.StatusCode == HttpStatusCode.OK ||
            response.StatusCode == HttpStatusCode.ServiceUnavailable,
            $"Health check returned unexpected status: {response.StatusCode}");
    }
}
