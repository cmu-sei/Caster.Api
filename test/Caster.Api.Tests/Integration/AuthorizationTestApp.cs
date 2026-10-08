// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Features.Files;
using Caster.Api.Features.Shared.Behaviors;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions.Middleware;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Infrastructure.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Npgsql;
using Directory = Caster.Api.Domain.Models.Directory;
using File = Caster.Api.Domain.Models.File;

namespace Caster.Api.Tests.Integration;

/// <summary>
/// An isolated relational database and HTTP host for the real controllers, handlers, mappings,
/// validators and authorization service. Only token authentication and Terraform version discovery
/// are substituted; background workers and external event delivery are outside these request tests.
/// </summary>
internal sealed class AuthorizationTestApp : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly DbContextOptions<CasterContext> _options;

    public Guid UserId { get; } = Guid.NewGuid();
    public Project ProjectA { get; } = new("A") { Id = Guid.NewGuid() };
    public Project ProjectB { get; } = new("B") { Id = Guid.NewGuid() };
    public Directory DirectoryA { get; }
    public Directory DirectoryB { get; }
    public Directory ChildA { get; }
    public Directory GrandchildA { get; }
    public Directory AlternateA { get; }
    public Workspace WorkspaceA { get; }
    public Workspace WorkspaceB { get; }
    public Workspace AlternateWorkspaceA { get; }
    public File FileA { get; }
    public File FileB { get; }
    public File AnotherFileA { get; }
    public File DeletedFileA { get; }
    public File DeletedFileB { get; }
    public Design DesignA { get; }
    public Design DesignB { get; }
    public Module Module { get; } = new() { Id = Guid.NewGuid(), Name = "catalog" };
    public DesignModule DesignModuleA { get; }
    public DesignModule DesignModuleB { get; }
    public HttpClient Client { get; }

    public AuthorizationTestApp(string serverConnectionString)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(serverConnectionString)
        {
            Database = $"caster_auth_{Guid.NewGuid():N}"
        }.ConnectionString;
        _options = new DbContextOptionsBuilder<CasterContext>().UseNpgsql(connectionString).Options;
        DirectoryA = new Directory("A") { Project = ProjectA, ProjectId = ProjectA.Id };
        DirectoryB = new Directory("B") { Project = ProjectB, ProjectId = ProjectB.Id };
        ChildA = new Directory("child", DirectoryA);
        GrandchildA = new Directory("grandchild", ChildA);
        AlternateA = new Directory("alternate") { Project = ProjectA, ProjectId = ProjectA.Id };
        WorkspaceA = NewWorkspace("workspace-a", DirectoryA);
        WorkspaceB = NewWorkspace("workspace-b", DirectoryB);
        AlternateWorkspaceA = NewWorkspace("alternate", AlternateA);
        FileA = NewFile("a.tf", DirectoryA, WorkspaceA);
        FileB = NewFile("b.tf", DirectoryB, WorkspaceB);
        AnotherFileA = NewFile("another.tf", DirectoryA);
        DeletedFileA = NewFile("deleted-a.tf", DirectoryA);
        DeletedFileA.IsDeleted = true;
        DeletedFileB = NewFile("deleted-b.tf", DirectoryB);
        DeletedFileB.IsDeleted = true;
        FileB.Tag("shared", UserId, DateTime.UtcNow);
        AnotherFileA.Tag("shared", UserId, DateTime.UtcNow);
        DesignA = new Design { Id = Guid.NewGuid(), Name = "design-a", Directory = DirectoryA, Enabled = false };
        DesignB = new Design { Id = Guid.NewGuid(), Name = "design-b", Directory = DirectoryB, Enabled = false };
        DesignModuleA = new DesignModule
        {
            Id = Guid.NewGuid(), Design = DesignA, Module = Module, Name = "before",
            ModuleVersion = "v1", Values = new List<ModuleValue> { new() { Name = "input", Value = "before" } }
        };
        DesignModuleB = new DesignModule
        {
            Id = Guid.NewGuid(), Design = DesignB, Module = Module, Name = "victim",
            ModuleVersion = "v1", Values = new List<ModuleValue> { new() { Name = "input", Value = "victim" } }
        };

        using (var db = NewContext())
        {
            db.Database.EnsureCreated();
            db.AddRange(
                new User { Id = UserId, Name = "actor" }, ProjectA, ProjectB,
                DirectoryA, DirectoryB, ChildA, GrandchildA, AlternateA,
                WorkspaceA, WorkspaceB, AlternateWorkspaceA,
                FileA, FileB, AnotherFileA, DeletedFileA, DeletedFileB,
                DesignA, DesignB, Module, DesignModuleA, DesignModuleB);
            db.SaveChanges();
        }

        _host = new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .UseEnvironment("Development")
            .ConfigureServices(services =>
            {
                services.AddDbContext<CasterContext>(options => options.UseNpgsql(connectionString));
                services.AddHttpContextAccessor();
                services.AddScoped<IIdentityResolver, IdentityResolver>();
                services.AddScoped<ICasterAuthorizationService, Infrastructure.Authorization.AuthorizationService>();
                services.AddScoped<IValidationService, ValidationService>();
                services.AddScoped<IGetFileQuery, GetFileQuery>();
                services.AddSingleton<ILockService, LockService>();
                services.AddSingleton(new TerraformOptions { MaxParallelism = 10 });
                var terraform = Substitute.For<ITerraformService>();
                terraform.IsValidVersion("1.5.7").Returns(true);
                services.AddSingleton(terraform);
                services.AddAutoMapper(typeof(Startup));
                services.AddMediatR(config =>
                {
                    config.RegisterServicesFromAssemblyContaining<Startup>();
                    config.AddOpenRequestPreProcessor(typeof(ValidationBehavior<>));
                });
                services.AddValidatorsFromAssemblyContaining<Startup>();
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
                services.AddAuthorization();
                services.AddSingleton<IAuthorizationHandler, SystemPermissionsHandler>();
                services.AddSingleton<IAuthorizationHandler, ProjectPermissionsHandler>();
                services.AddSingleton<IAuthorizationHandler, GroupPermissionsHandler>();
                services.AddControllers().AddApplicationPart(typeof(Startup).Assembly)
                    .AddJsonOptions(options =>
                    {
                        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                        options.JsonSerializerOptions.Converters.Add(new OptionalGuidConverter());
                        options.JsonSerializerOptions.Converters.Add(new OptionalIntConverter());
                    });
            })
            .Configure(app =>
            {
                app.UseCustomExceptionHandler();
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }))
            // Assembly scanning also registers unrelated handlers for external integrations.
            // Resolve handlers only when a test invokes their real HTTP route.
            .UseDefaultServiceProvider(options => options.ValidateOnBuild = false)
            .Build();
        _host.Start();
        Client = _host.GetTestClient();
        Client.DefaultRequestHeaders.Add("Test-User", UserId.ToString());
        AsProjectEditor();
    }

    public CasterContext NewContext() => new(_options);

    public void AsProjectEditor() => AsProjectPermission(ProjectPermission.EditProject);

    public void AsProjectPermission(ProjectPermission? permission)
    {
        Client.DefaultRequestHeaders.Remove("Test-System");
        Client.DefaultRequestHeaders.Remove("Test-Project");
        Client.DefaultRequestHeaders.Remove("Test-Permission");
        if (permission.HasValue)
        {
            Client.DefaultRequestHeaders.Add("Test-Project", ProjectA.Id.ToString());
            Client.DefaultRequestHeaders.Add("Test-Permission", permission.Value.ToString());
        }
    }

    public void AsAdmin()
    {
        AsProjectPermission(null);
        Client.DefaultRequestHeaders.Add("Test-System", SystemPermission.EditProjects.ToString());
    }

    private static Workspace NewWorkspace(string name, Directory directory) => new(name, directory)
    {
        Id = Guid.NewGuid(), State = "sensitive-state", StateBackup = "sensitive-backup"
    };

    private File NewFile(string name, Directory directory, Workspace workspace = null)
    {
        var file = new File { Id = Guid.NewGuid(), Name = name, Directory = directory, Workspace = workspace, Content = name };
        file.Save(UserId, canLock: true, bypassLock: true);
        file.Lock(UserId, canLock: true);
        return file;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }
}

internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim> { new("sub", Request.Headers["Test-User"].ToString()) };
        if (Guid.TryParse(Request.Headers["Test-Project"], out var projectId)
            && Enum.TryParse<ProjectPermission>(Request.Headers["Test-Permission"], out var permission))
        {
            claims.Add(new Claim(AuthorizationConstants.ProjectPermissionsClaimType,
                new ProjectPermissionsClaim { ProjectId = projectId, Permissions = [permission] }.ToString()));
        }

        if (Request.Headers.TryGetValue("Test-System", out var systemPermission))
            claims.Add(new Claim(AuthorizationConstants.PermissionsClaimType, systemPermission.ToString()));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
