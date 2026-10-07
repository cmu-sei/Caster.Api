// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// GET api/users answers 403 to a caller holding none of ViewUsers, ViewProjects, a ManageProject membership
// or a managed group; POST api/groups creates a row in a uniquely named table.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Tests.Support;

/// <summary>
/// Tests for the HTTP harness itself: a harness that routes to the wrong database or authorizes everything
/// reads as a green suite.
/// </summary>
public class HttpHarnessTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    private const string SharedName = "Http Isolation Probe";

    [Fact]
    public async Task The_swagger_document_is_served()
    {
        await AssertStatus(HttpStatusCode.OK, await Client().GetAsync("/swagger/v1/swagger.json", Ct));
    }

    [Fact]
    public async Task A_request_with_no_identity_is_unauthorized()
    {
        await AssertStatus(HttpStatusCode.Unauthorized, await Client().GetAsync("api/users", Ct));
    }

    /// <summary>The claims transformer ran and derived nothing, rather than the pipeline granting by default.</summary>
    [Fact]
    public async Task An_actor_with_no_permissions_is_forbidden()
    {
        var actor = await Actor().SeedAsync();

        await AssertStatus(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/users", Ct));
    }

    /// <summary>The seeded administrator role becomes real claims, and the request reads this test's database.</summary>
    [Fact]
    public async Task Root_reads_what_the_test_seeded()
    {
        var response = await RootClient.GetAsync("api/users", Ct);

        var users = await ReadAsync<List<IdOnly>>(response);
        Assert.Contains(Root.Id, users.Select(x => x.Id));
    }

    [Fact]
    public Task Concurrent_tests_share_the_host_but_not_the_database_first() => CreateSharedNameGroup();

    [Fact]
    public Task Concurrent_tests_share_the_host_but_not_the_database_second() => CreateSharedNameGroup();

    private async Task CreateSharedNameGroup()
    {
        var response = await RootClient.PostAsJsonAsync("api/groups", new { name = SharedName }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);

        await using var context = NewContext();
        Assert.Equal(1, await context.Groups.CountAsync(x => x.Name == SharedName, Ct));
    }

    private sealed record IdOnly(Guid Id);
}
