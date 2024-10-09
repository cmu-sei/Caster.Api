// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Threading;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.HttpHandlers;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Infrastructure.Swashbuckle.OperationFilters;
using Caster.Api.Infrastructure.Swashbuckle.ParameterFilters;
using Caster.Api.Infrastructure.Swashbuckle.SchemaFilters;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Polly;
using Polly.Extensions.Http;
using Player.Vm.Api;
using Caster.Api.Infrastructure.Swashbuckle.DocumentFilters;

namespace Caster.Api.Infrastructure.Extensions
{
    public static class ServiceCollectionExtensions
    {
        #region Api Clients

        public static void AddApiClients(this IServiceCollection services, ClientOptions clientOptions, TerraformOptions terraformOptions, ILoggerFactory loggerFactory)
        {
            services.AddPlayerClient(clientOptions, loggerFactory);
            services.AddIdentityClient(clientOptions, loggerFactory);
            services.AddGitlabClient(clientOptions, terraformOptions, loggerFactory);

            services.AddTransient<AuthenticatingHandler>();
        }

        private static IAsyncPolicy<HttpResponseMessage> GetPolicy(int maxRetryDelaySeconds, string serviceName, ILoggerFactory loggerFactory)
        {
            var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryForeverAsync(
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryAttempt), maxRetryDelaySeconds)),
                onRetry: (exception, retryAttempt, calculatedWaitDuration) =>
                {
                    var logger = loggerFactory.CreateLogger<Policy>();
                    logger.LogError(exception.Exception, $"Attempt {retryAttempt}. Retrying connection to {serviceName}");
                });

            return retryPolicy;
        }

        private static void AddPlayerClient(this IServiceCollection services, ClientOptions clientOptions, ILoggerFactory loggerFactory)
        {
            var policy = GetPolicy(clientOptions.MaxRetryDelaySeconds, "Player Vm Api", loggerFactory);

            services.AddHttpClient("player", client =>
            {
                // Workaround to avoid TaskCanceledException after several retries. TODO: find a better way to handle this.
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddHttpMessageHandler<AuthenticatingHandler>()
            .AddPolicyHandler(policy);

            services.AddScoped<IPlayerVmApiClient, PlayerVmApiClient>(p =>
            {
                var httpClientFactory = p.GetRequiredService<IHttpClientFactory>();
                var playerOptions = p.GetRequiredService<PlayerOptions>();

                var uri = new Uri(playerOptions.VmApiUrl);

                var httpClient = httpClientFactory.CreateClient("player");
                httpClient.BaseAddress = uri;

                var playerVmApiClient = new PlayerVmApiClient(httpClient, true)
                {
                    BaseUri = uri
                };

                return playerVmApiClient;
            });
        }

        private static void AddIdentityClient(this IServiceCollection services, ClientOptions clientOptions, ILoggerFactory loggerFactory)
        {
            var policy = GetPolicy(clientOptions.MaxRetryDelaySeconds, "Identity", loggerFactory);

            services.AddHttpClient("identity", client =>
            {
                // Workaround to avoid TaskCanceledException after several retries. TODO: find a better way to handle this.
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddPolicyHandler(policy);
        }

        private static void AddGitlabClient(this IServiceCollection services, ClientOptions clientOptions, TerraformOptions terraformOptions, ILoggerFactory loggerFactory)
        {
            var policy = GetPolicy(clientOptions.MaxRetryDelaySeconds, "Gitlab Api", loggerFactory);
            services.AddHttpClient("gitlab", client =>
            {
                // Workaround to avoid TaskCanceledException after several retries. TODO: find a better way to handle this.
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.BaseAddress = new Uri(terraformOptions.GitlabApiUrl);
            }).AddPolicyHandler(policy);

        }


        #endregion

        #region Swagger

        public static void AddSwagger(this IServiceCollection services, AuthorizationOptions authOptions)
        {
            // XML Comments path
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var commentsFileName = Assembly.GetExecutingAssembly().GetName().Name + ".xml";
            var commentsFilePath = Path.Combine(baseDirectory, commentsFileName);

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Caster API", Version = "v1.1.0" });
                c.CustomSchemaIds(schemaIdStrategy);
                c.OperationFilter<JsonIgnoreQueryOperationFilter>();
                c.OperationFilter<DefaultResponseOperationFilter>();
                c.SchemaFilter<AutoRestEnumSchemaFilter>();
                c.ParameterFilter<AutoRestEnumParameterFilter>();
                c.DocumentFilter<ValidationProblemDetailsDocumentFilter>();

                c.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
                {
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.OAuth2,
                    Flows = new OpenApiOAuthFlows
                    {
                        AuthorizationCode = new OpenApiOAuthFlow
                        {
                            AuthorizationUrl = new Uri(authOptions.AuthorizationUrl),
                            TokenUrl = new Uri(authOptions.TokenUrl),
                            Scopes = new Dictionary<string, string>()
                            {
                                {authOptions.AuthorizationScope, "public api access"}
                            }
                        }
                    }
                });


                c.AddSecurityRequirement(new OpenApiSecurityRequirement()
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "oauth2"
                            },
                            Scheme = "oauth2"
                        },
                        new[] {authOptions.AuthorizationScope}
                    }
                });

                c.EnableAnnotations();

                c.IncludeXmlComments(commentsFilePath);
                c.MapType<Optional<Guid?>>(() => new OpenApiSchema { Type = "string", Format = "uuid" });
                c.MapType<Optional<int?>>(() => new OpenApiSchema { Type = "integer", Format = "int32", Nullable = true });
                c.MapType<JsonElement?>(() => new OpenApiSchema { Type = "object", Nullable = true });
            });
        }

        private static string schemaIdStrategy(Type currentClass)
        {
            var dataContractAttribute = currentClass.GetCustomAttribute<DataContractAttribute>();
            return dataContractAttribute != null && dataContractAttribute.Name != null ? dataContractAttribute.Name : currentClass.Name;
        }

        #endregion
    }
}
