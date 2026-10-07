// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// Caster's migrations live in the Caster.Api assembly beside CasterContext, so no MigrationsAssembly is
// named. Program.Main has no switch that skips InitializeDatabase, so the host gets a database of its own
// (the run-wide factory's step 1B).

using System;
using System.Threading.Tasks;
using Caster.Api.Data;
using Xunit;

namespace Caster.Api.Tests.Support;

/// <summary>
/// Owns the PostgreSQL database for the whole test run: starts it on first use and hands out an isolated
/// session per test.
/// </summary>
/// <remarks>
/// PostgreSQL exercises production's actual database, including the <c>if (Database.IsNpgsql())</c>
/// branch of <c>CasterContext.OnModelCreating</c> and the real migration history. A usable Docker daemon
/// is therefore required by every test that takes a database. The mechanics are the shared
/// <see cref="PostgresTestDatabase{TContext}"/>.
/// </remarks>
public sealed class DatabaseFixture : IAsyncLifetime, ITestDatabaseSessionSource<CasterContext>
{
    private static readonly PostgresTestDatabase<CasterContext> _database = new(new()
    {
        Name = "caster",
        TestAssembly = "Caster.Api.Tests",
        CreateContext = CasterContextFactory.CreateContext,
        CreateServices = CasterContextFactory.CreateServices
    });

    /// <summary>
    /// The database <see cref="CasterAppFactory"/> hands to <c>Program.Main</c>, whose
    /// <c>InitializeDatabase</c> migrates (a no-op on a clone of the template) and seeds it. Lazy, and
    /// blocking only inside <c>ConfigureWebHost</c>, which runs when the first test uses the host, so tests
    /// that need no database still run without Docker. Never dropped: the container goes at the end.
    /// </summary>
    private static readonly Lazy<Task<ITestDatabaseSession<CasterContext>>> _host =
        new(() => _database.BeginSessionAsync());

    public static ITestDatabaseSession<CasterContext> HostDatabase() => _host.Value.GetAwaiter().GetResult();

    /// <summary>Nothing to do here: the container starts on the first request for a session.</summary>
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public Task<ITestDatabaseSession<CasterContext>> BeginSessionAsync() => _database.BeginSessionAsync();

    public ValueTask DisposeAsync() => _database.DisposeAsync();
}
