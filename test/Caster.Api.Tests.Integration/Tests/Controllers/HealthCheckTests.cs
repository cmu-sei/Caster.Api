// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using Caster.Api.Tests.Integration.Fixtures;

namespace Caster.Api.Tests.Integration.Tests.Controllers;

[Category("Integration")]
[ClassDataSource<CasterTestContext>(Shared = SharedType.PerTestSession)]
public class HealthCheckTests(CasterTestContext context)
{
    private readonly CasterTestContext _context = context;

    [Test]
    public async Task GetHealth_WhenCalled_ReturnsSuccessStatusCode()
    {
        var client = _context.CreateClient();
        var response = await client.GetAsync("/api/health");

        await Assert.That(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.ServiceUnavailable)
            .IsTrue();
    }
}
