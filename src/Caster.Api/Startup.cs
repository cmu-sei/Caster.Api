// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AutoMapper.Internal;
using Caster.Api.Data;
using Caster.Api.Domain.Services;
using Caster.Api.Extensions;
using Caster.Api.Features.Files;
using Caster.Api.Features.Shared;
using Caster.Api.Features.Shared.Behaviors;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Hubs;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.ClaimsTransformers;
using Caster.Api.Infrastructure.DbInterceptors;
using Caster.Api.Infrastructure.Exceptions.Middleware;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Infrastructure.Mapping;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Infrastructure.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

[assembly: ApiController]
namespace Caster.Api
{
    public class Startup
    {
        public IConfiguration Configuration { get; }

        private readonly AuthorizationOptions _authOptions = new AuthorizationOptions();
        private readonly ClientOptions _clientOptions = new ClientOptions();
        private readonly TerraformOptions _terraformOptions = new TerraformOptions();
        private readonly ILoggerFactory _loggerFactory;
        private string _pathbase;
        private readonly TelemetryOptions _telemetryOptions = new();
        private const string _routePrefix = "api";

        public Startup(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            Configuration = configuration;
            Configuration.GetSection("Authorization").Bind(_authOptions);
            Configuration.GetSection("Client").Bind(_clientOptions);
            Configuration.GetSection("Terraform").Bind(_terraformOptions);
            Configuration.GetSection("Telemetry").Bind(_telemetryOptions);
            _pathbase = Configuration["PathBase"] ?? "";

            _loggerFactory = loggerFactory;
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<HostedServiceHealthCheck>();
            services.AddHealthChecks()
                .AddCheck<HostedServiceHealthCheck>(
                    "service_responsive",
                    failureStatus: HealthStatus.Unhealthy,
                    tags: new[] { "live" });

            services.AddPooledDbContextFactory<CasterContext>((serviceProvider, builder) =>
                builder.AddInterceptors(serviceProvider.GetRequiredService<EventInterceptor>())
                .UseNpgsql(Configuration.GetConnectionString("PostgreSQL")));

            services.AddScoped<CasterContextFactory>();
            services.AddScoped(sp => sp.GetRequiredService<CasterContextFactory>().CreateDbContext());

            services.AddHealthChecks().AddNpgSql(Configuration.GetConnectionString("PostgreSQL"), tags: new[] { "ready", "live" });
            services.AddCors(options => options.UseConfiguredCors(Configuration.GetSection("CorsPolicy")));

            services.AddOptions()
                .Configure<TerraformOptions>(Configuration.GetSection("Terraform"))
                    .AddScoped(config => config.GetService<IOptionsMonitor<TerraformOptions>>().CurrentValue);

            services.AddOptions()
                .Configure<ClientOptions>(Configuration.GetSection("Client"))
                    .AddScoped(config => config.GetService<IOptionsMonitor<ClientOptions>>().CurrentValue);

            services.AddOptions()
                .Configure<PlayerOptions>(Configuration.GetSection("Player"))
                    .AddScoped(config => config.GetService<IOptionsMonitor<PlayerOptions>>().CurrentValue);

            services.AddOptions()
                .Configure<ClaimsTransformationOptions>(Configuration.GetSection("ClaimsTransformation"))
                    .AddScoped(config => config.GetService<IOptionsMonitor<ClaimsTransformationOptions>>().CurrentValue);

            services.AddOptions()
                .Configure<SeedDataOptions>(Configuration.GetSection("SeedData"))
                    .AddScoped(config => config.GetService<IOptionsMonitor<SeedDataOptions>>().CurrentValue);

            services.AddOptions()
                .Configure<FileVersionScrubOptions>(Configuration.GetSection("FileVersions"))
                    .AddScoped(config => config.GetService<IOptionsMonitor<FileVersionScrubOptions>>().CurrentValue);


            services.AddMvc()
            .AddJsonOptions(options =>
            {
                // must be synced with DefaultJsonSettings.cs
                options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumMemberConverter());
                options.JsonSerializerOptions.Converters.Add(new OptionalGuidConverter());
                options.JsonSerializerOptions.Converters.Add(new OptionalIntConverter());
            });

            services.AddSignalR()
                .AddJsonProtocol(options =>
                {
                    // must be synced with DefaultJsonSettings.cs
                    options.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
                    options.PayloadSerializerOptions.AllowTrailingCommas = true;
                    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumMemberConverter());
                    options.PayloadSerializerOptions.Converters.Add(new OptionalGuidConverter());
                    options.PayloadSerializerOptions.Converters.Add(new OptionalIntConverter());
                });

            services.AddSwagger(_authOptions);

            services.AddAutoMapper(cfg =>
            {
                cfg.Internal().ForAllPropertyMaps(
                    pm => pm.SourceType != null && Nullable.GetUnderlyingType(pm.SourceType) == pm.DestinationType,
                    (pm, c) => c.MapFrom<object, object, object, object>(new IgnoreNullSourceValues(), pm.SourceMember.Name));
            }, typeof(Startup));

            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssemblyContaining<Startup>();
                cfg.AddOpenRequestPreProcessor(typeof(ValidationBehavior<>));
            });

            // Adding Fluent Validation here so we don't get errors when requests aren't fully populated by the controller
            services.AddValidatorsFromAssemblyContaining<Startup>();

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.Authority = _authOptions.Authority;
                    options.RequireHttpsMetadata = _authOptions.RequireHttpsMetadata;
                    options.SaveToken = true;

                    string[] validAudiences;

                    if (_authOptions.ValidAudiences != null && _authOptions.ValidAudiences.Any())
                    {
                        validAudiences = _authOptions.ValidAudiences;
                    }
                    else
                    {
                        validAudiences = _authOptions.AuthorizationScope.Split(' ');
                    }

                    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                    {
                        ValidateAudience = _authOptions.ValidateAudience,
                        ValidAudiences = validAudiences
                    };

                    // We have to hook the OnMessageReceived event in order to
                    // allow the JWT authentication handler to read the access
                    // token from the query string when a WebSocket or
                    // Server-Sent Events request comes in.

                    // Sending the access token in the query string is required due to
                    // a limitation in Browser APIs. We restrict it to only calls to the
                    // SignalR hub in this code.
                    // See https://docs.microsoft.com/aspnet/core/signalr/security#access-token-logging
                    // for more information about security considerations when using
                    // the query string to transmit the access token.
                    options.Events = new JwtBearerEvents
                    {
                        OnMessageReceived = context =>
                        {
                            // If the request is for our hub...
                            var path = context.HttpContext.Request.Path;
                            var accessToken = context.Request.Query["access_token"];

                            if (!string.IsNullOrEmpty(accessToken) &&
                                (path.StartsWithSegments("/hubs")))
                            {
                                // Read the token out of the query string
                                context.Token = accessToken;
                            }
                            return Task.CompletedTask;
                        }
                    };
                });

            JsonWebTokenHandler.DefaultInboundClaimTypeMap.Clear();

            services.AddAuthorizationPolicy(_authOptions);

            services.AddApiClients(_clientOptions, _terraformOptions, _loggerFactory);

            services.AddScoped<ITerraformService, TerraformService>();
            services.AddScoped<IClaimsTransformation, AuthorizationClaimsTransformer>();
            services.AddScoped<IUserClaimsService, UserClaimsService>();
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddScoped<IIdentityResolver, IdentityResolver>();
            services.AddScoped<IGitlabRepositoryService, GitlabRepositoryService>();
            services.AddScoped<IArchiveService, ArchiveService>();
            services.AddScoped<IImportService, ImportService>();
            services.AddScoped<IValidationService, ValidationService>();
            services.AddScoped<ICasterAuthorizationService, AuthorizationService>();

            services.AddSingleton<Domain.Services.IAuthenticationService, Domain.Services.AuthenticationService>();
            services.AddSingleton<ILockService, LockService>();
            services.AddSingleton<IUserIdProvider, SubUserIdProvider>();
            services.AddSingleton<IOutputService, OutputService>();

            services.AddSingleton<IFileVersionScrubService, FileVersionScrubService>();
            services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(x => x.GetService<IFileVersionScrubService>());

            services.AddSingleton<IPlayerSyncService, PlayerSyncService>();
            services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(x => x.GetService<IPlayerSyncService>());

            services.AddSingleton<IRunQueueService, RunQueueService>();
            services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(x => x.GetService<IRunQueueService>());

            services.AddScoped<IGetFileQuery, GetFileQuery>();
            services.AddTransient<EventInterceptor>();

            services.AddSingleton<TelemetryService>();
            var metricsBuilder = services.AddOpenTelemetry()
                .WithMetrics(builder =>
                {
                    builder
                        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("TelemetryService"))
                        .AddMeter
                        (
                            TelemetryService.CasterMeterName
                        )
                        .AddPrometheusExporter();
                    if (_telemetryOptions.AddAspNetCoreInstrumentation)
                    {
                        builder.AddAspNetCoreInstrumentation();
                    }
                    if (_telemetryOptions.AddHttpClientInstrumentation)
                    {
                        builder.AddHttpClientInstrumentation();
                    }
                    if (_telemetryOptions.UseMeterMicrosoftAspNetCoreHosting)
                    {
                        builder.AddMeter("Microsoft.AspNetCore.Hosting");
                    }
                    if (_telemetryOptions.UseMeterMicrosoftAspNetCoreServerKestrel)
                    {
                        builder.AddMeter("Microsoft.AspNetCore.Server.Kestrel");
                    }
                    if (_telemetryOptions.UseMeterSystemNetHttp)
                    {
                        builder.AddMeter("System.Net.Http");
                    }
                    if (_telemetryOptions.UseMeterSystemNetNameResolution)
                    {
                        builder.AddMeter("System.Net.NameResolution");
                    }
                }
            );
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UsePathBase(_pathbase);

            app.UseCustomExceptionHandler();
            app.UseRouting();
            app.UseCors();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHealthChecks($"/{_routePrefix}/health/ready", new HealthCheckOptions()
                {
                    Predicate = (check) => check.Tags.Contains("ready"),
                });

                endpoints.MapHealthChecks($"/{_routePrefix}/health/live", new HealthCheckOptions()
                {
                    Predicate = (check) => check.Tags.Contains("live"),
                });
                endpoints.MapHub<ProjectHub>("/hubs/project");
                endpoints.MapPrometheusScrapingEndpoint().RequireAuthorization();
            });

            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint($"{_pathbase}/swagger/v1/swagger.json", "Caster API V1");
                c.RoutePrefix = _routePrefix;
                c.OAuthClientId(_authOptions.ClientId);
                c.OAuthClientSecret(_authOptions.ClientSecret);
                c.OAuthAppName(_authOptions.ClientName);
                c.OAuthUsePkce();
            });
        }
    }
}
