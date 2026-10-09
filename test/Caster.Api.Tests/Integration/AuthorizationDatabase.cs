// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading.Tasks;
using Testcontainers.PostgreSql;
using Xunit;

namespace Caster.Api.Tests.Integration;

/// <summary>
/// One disposable PostgreSQL server for the HTTP suite. Each test creates and removes its own
/// database, so neither the running development AppHost nor any development data is used.
/// </summary>
public sealed class AuthorizationDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17.6").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}
