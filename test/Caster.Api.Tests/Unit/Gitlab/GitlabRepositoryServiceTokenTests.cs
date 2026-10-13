// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Caster.Api.Tests.Unit.Gitlab
{
    /// <summary>
    /// The GitLab token must travel in the PRIVATE-TOKEN header, never in a
    /// request url, where proxies, access logs and HttpClient logging record it.
    /// </summary>
    [Trait("Category", "Unit")]
    public class GitlabRepositoryServiceTokenTests
    {
        private const string Token = "glpat-secret-value";

        [Fact]
        public async Task SendsTokenInHeaderAndNeverInUrl()
        {
            var handler = new RecordingHandler();
            var service = CreateService(handler, Token);

            await service.GetModuleAsync("7", CancellationToken.None);

            // projects/7, releases, then outputs and variables for the one release.
            Assert.Equal(4, handler.Requests.Count);

            foreach (var request in handler.Requests)
            {
                Assert.True(request.Headers.TryGetValues("PRIVATE-TOKEN", out var values));
                Assert.Equal(Token, values.Single());
                Assert.DoesNotContain("private_token", request.RequestUri.ToString());
                Assert.DoesNotContain(Token, request.RequestUri.ToString());
            }
        }

        [Fact]
        public async Task SendsNoTokenHeaderWhenNoTokenIsConfigured()
        {
            var handler = new RecordingHandler();
            var service = CreateService(handler, null);

            await service.GetModuleAsync("7", CancellationToken.None);

            Assert.NotEmpty(handler.Requests);
            Assert.All(handler.Requests, r => Assert.False(r.Headers.Contains("PRIVATE-TOKEN")));
        }

        /// <summary>
        /// Builds the service on the real "gitlab" client registration from
        /// AddApiClients, swapping only the primary handler, so the header
        /// logic under test is the production one.
        /// </summary>
        private static GitlabRepositoryService CreateService(HttpMessageHandler handler, string token)
        {
            var terraformOptions = new TerraformOptions
            {
                GitlabApiUrl = "https://gitlab.example/api/v4/",
                GitlabToken = token,
                GitlabGroupId = 1,
            };

            var services = new ServiceCollection();
            services.AddLogging();
            services.Configure<TerraformOptions>(o =>
            {
                o.GitlabApiUrl = terraformOptions.GitlabApiUrl;
                o.GitlabToken = terraformOptions.GitlabToken;
                o.GitlabGroupId = terraformOptions.GitlabGroupId;
            });
            services.AddApiClients(new ClientOptions(), terraformOptions);
            services.AddHttpClient("gitlab").ConfigurePrimaryHttpMessageHandler(() => handler);

            var provider = services.BuildServiceProvider();

            var builder = new DbContextOptionsBuilder<CasterContext>();
            builder.UseInMemoryDatabase($"caster_gitlab_{Guid.NewGuid():N}");

            return new GitlabRepositoryService(
                new CasterContext(builder.Options),
                null,
                provider.GetRequiredService<IOptionsMonitor<TerraformOptions>>(),
                provider.GetRequiredService<IHttpClientFactory>());
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            public List<HttpRequestMessage> Requests { get; } = [];

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Requests.Add(request);

                var path = request.RequestUri.AbsolutePath;

                if (path.EndsWith("/projects/7"))
                {
                    return Json("""
                        {"id":7,"name":"network","path_with_namespace":"modules/network",
                         "description":"d","last_activity_at":"2026-01-01T00:00:00Z",
                         "http_url_to_repo":"https://gitlab.example/modules/network.git"}
                        """);
                }

                if (path.EndsWith("/releases"))
                {
                    return Json("""[{"tag_name":"v1.0.0","released_at":"2026-01-01T00:00:00Z"}]""");
                }

                // outputs.tf.json / variables.tf.json: absent is a supported case.
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            private static Task<HttpResponseMessage> Json(string body) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                });
        }
    }
}
