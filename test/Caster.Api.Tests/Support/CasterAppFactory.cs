// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// The run-wide factory with the template's step 1B: Caster's Program.Main always runs InitializeDatabase,
// so the host is given a throwaway database of its own. One hub (ProjectHub), one IHttpClientFactory
// (Player VM API, identity and GitLab clients), and four hosted services.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using Caster.Api.Data;
using Caster.Api.Hubs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Caster.Api.Tests.Support;

/// <summary>
/// Hosts <c>Caster.Api</c> in process over <c>TestServer</c>, so that tests drive the real application: the
/// real <c>Startup</c>, the real middleware chain, the real MediatR handlers and validators, the real
/// authorization stack and the real claims transformer.
/// </summary>
/// <remarks>
/// <para>
/// One instance serves the whole run, declared in <c>AssemblyFixtures.cs</c>. Everything the application
/// registers as a singleton is therefore shared by every test, which is what
/// <see cref="TestConfiguration"/>'s claims-caching entry and <see cref="TestDatabaseScope"/> exist to
/// deal with.
/// </para>
/// <para>
/// <c>Program.CreateWebHostBuilder</c> matches neither convention <c>HostFactoryResolver</c> looks for, so
/// <c>WebApplicationFactory</c> runs <c>Program.Main</c> on a background thread, and <c>Main</c> runs
/// <c>InitializeDatabase</c> (migrate, then seed from <c>SeedData</c>) before the host would start. Caster
/// has no switch that skips it, so <c>ConnectionStrings:PostgreSQL</c> names a database cloned from the
/// migrated template for this host alone; migrating it is a no-op and the shipped <c>SeedData</c> is empty.
/// Requests never use it: <see cref="TestDatabaseScope"/> routes each one to its test's database.
/// </para>
/// <para>
/// Only three things are not the application's own: token validation (<see cref="TestAuthHandler"/>), the
/// context registration (<see cref="TestDatabaseScope"/>), and the collaborators that leave the process
/// (the hub context, outbound HTTP, the hosted services).
/// </para>
/// </remarks>
public sealed class CasterAppFactory : WebApplicationFactory<Program>, ITestHttpHost
{
    private readonly ConcurrentDictionary<Type, object> _hubs = new();

    /// <summary>
    /// Answers every request the application makes over HTTP (the Player VM API, the identity provider,
    /// GitLab). Arrange a url of your own on it, then assert on what was requested.
    /// </summary>
    public StubHttpMessageHandler OutboundHttp { get; } = new();

    /// <summary>
    /// What the application broadcast through a hub. Held here rather than resolved from the container, so
    /// a test reads the instance the request wrote to.
    /// </summary>
    public HubRecorder<THub> Hub<THub>() where THub : Hub =>
        (HubRecorder<THub>)_hubs.GetOrAdd(typeof(THub), _ => new HubRecorder<THub>());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Production, so the developer exception page stays off and ExceptionMiddleware answers as it does
        // in a deployment: a 500's body carries the exception message as its detail, not the stack trace.
        builder.UseEnvironment("Production");

        // Step 1B: Main's InitializeDatabase migrates and seeds this database. See the remarks above.
        builder.UseSetting("ConnectionStrings:PostgreSQL", DatabaseFixture.HostDatabase().ConnectionString);

        builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(TestConfiguration.Values));

        builder.ConfigureTestServices(services =>
        {
            // FileVersionScrubService, PlayerSyncService, ImageTagService and RunQueueService start in the
            // background and reach for databases and services no test owns. IRunQueueService stays
            // registered, so a created run is queued and never executed.
            services.RemoveAll<IHostedService>();

            services
                .AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);

            // InitializeDatabase resolves the context from a scope of its own, outside any request. Until
            // the host has started, such a resolution gets the host's own database, over that session's
            // own services, so the seed's entity events never reach the real handlers or a recorder;
            // afterwards it throws, so a stray resolution outside a request still fails loudly.
            TestDatabaseScope.ReplaceRegistration<CasterContext>(
                services, () => _started ? null : DatabaseFixture.HostDatabase());

            // SignalR registers hub contexts as an open generic, which RemoveAll of a closed type cannot
            // match; a later closed registration wins on resolution.
            services.AddSingleton<IHubContext<ProjectHub>>(Hub<ProjectHub>());

            services.RemoveAll<IHttpClientFactory>();
            // Replacing the factory drops the "gitlab" client's AddHttpClient configuration, whose base
            // address GitlabRepositoryService's relative urls need.
            services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(
                OutboundHttp,
                new Dictionary<string, Uri> { ["gitlab"] = new(TestConfiguration.GitlabApiUrl) }));
        });
    }

    /// <summary>Set once the host has started, after <c>Program.Main</c>'s <c>InitializeDatabase</c>.</summary>
    private volatile bool _started;

    private readonly object _hostGate = new();
    private IHost _host;

    /// <remarks>
    /// <c>WebApplicationFactory</c> starts its server on the first <c>CreateClient</c> without a lock, so two
    /// tests starting together would each build a host, and the first to finish would close the startup
    /// window below while the second's <c>InitializeDatabase</c> is still running. One host is built, under a
    /// lock, and handed to every caller.
    /// </remarks>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        lock (_hostGate)
        {
            if (_host is null)
            {
                _host = base.CreateHost(builder);
                _started = true;
            }

            return _host;
        }
    }

    public override async System.Threading.Tasks.ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        TestConfiguration.RemoveTerraformDirectories();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            TestConfiguration.RemoveTerraformDirectories();
        }
    }
}
