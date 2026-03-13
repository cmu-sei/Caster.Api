// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Data;
using Crucible.Common.Testing.Auth;
using Crucible.Common.Testing.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace Caster.Api.Tests.Integration.Fixtures;

public class CasterTestContext : WebApplicationFactory<Program>, IAsyncInitializer, IAsyncDisposable
{
    private PostgreSqlContainer? _container;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder
            .UseEnvironment("Test")
            .ConfigureServices(services =>
            {
                if (_container is null)
                    throw new InvalidOperationException("Database container not initialized");

                services.RemoveAll<IDbContextFactory<CasterContext>>();
                services.RemoveAll<DbContextOptions<CasterContext>>();

                services.AddDbContext<CasterContext>(opts =>
                    opts.UseNpgsql(_container.GetConnectionString()));

                services
                    .ReplaceService<IClaimsTransformation, TestClaimsTransformation>(allowMultipleReplace: true)
                    .ReplaceService<IAuthorizationService, TestAuthorizationService>();

                services.AddAuthentication(TestAuthenticationHandler.AuthenticationSchemeName)
                    .AddScheme<TestAuthenticationHandlerOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.AuthenticationSchemeName, _ => { });
            });
    }

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:latest")
            .WithAutoRemove(true)
            .WithCleanUp(true)
            .Build();

        await _container.StartAsync();
    }

    public new async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
