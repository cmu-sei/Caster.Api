// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Caster.Api.Tests.Domain.Services;

/// <summary>
/// The GitLab token travels in the PRIVATE-TOKEN header, never in a request url, where proxies, access logs
/// and HttpClient logging record it.
/// </summary>
/// <remarks>
/// The service is built on the real "gitlab" client registration from <c>AddApiClients</c>, with only the
/// primary handler swapped, so the header logic under test is the production one. The run-wide host replaces
/// <see cref="IHttpClientFactory"/> and with it that registration, which is why these tests build their own
/// container.
/// </remarks>
public class GitlabRepositoryServiceTokenTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private const string Token = "glpat-secret-value";

    /// <summary>The project, its releases, then the outputs and variables files of its one release.</summary>
    private static readonly string[] ModuleRequests =
    [
        "api/v4/projects/7",
        "api/v4/projects/7/releases",
        "api/v4/projects/7/repository/files/outputs.tf.json/raw",
        "api/v4/projects/7/repository/files/variables.tf.json/raw",
    ];

    [Fact]
    public async Task SendsTokenInHeaderAndNeverInUrl()
    {
        var handler = GitlabWithOneModule();
        using var provider = Services(handler, Token);

        await Service(provider).GetModuleAsync("7", Ct);

        Assert.Equal(ModuleRequests, handler.Paths);
        Assert.All(handler.Sent, request =>
        {
            Assert.Equal(Token, request.Headers["PRIVATE-TOKEN"]);
            Assert.DoesNotContain("private_token", request.Uri);
            Assert.DoesNotContain(Token, request.Uri);
        });
    }

    /// <summary>The group listing a module sync starts from, and the releases and files of the module it lists.</summary>
    [Fact]
    public async Task SendsTokenInHeaderAndNeverInUrlWhenListingTheGroup()
    {
        var handler = GitlabWithOneModule();
        using var provider = Services(handler, Token);

        await Service(provider).GetModulesAsync(forceUpdate: true, Ct);

        Assert.Equal(GroupRequests, handler.Paths);
        Assert.Equal("?include_subgroups=true", handler.Sent[0].Query);
        Assert.All(handler.Sent, request =>
        {
            Assert.Equal(Token, request.Headers["PRIVATE-TOKEN"]);
            Assert.DoesNotContain("private_token", request.Uri);
            Assert.DoesNotContain(Token, request.Uri);
        });
    }

    [Fact]
    public async Task SendsNoTokenHeaderWhenNoTokenIsConfigured()
    {
        var handler = GitlabWithOneModule();
        using var provider = Services(handler, null);

        await Service(provider).GetModuleAsync("7", Ct);

        Assert.Equal(ModuleRequests, handler.Paths);
        Assert.All(handler.Sent, request => Assert.False(request.Headers.ContainsKey("PRIVATE-TOKEN")));
    }

    /// <summary>The group's project list, then the releases and files of the one project it lists.</summary>
    private static readonly string[] GroupRequests =
    [
        "api/v4/groups/1/projects",
        "api/v4/projects/7/releases",
        "api/v4/projects/7/repository/files/outputs.tf.json/raw",
        "api/v4/projects/7/repository/files/variables.tf.json/raw",
    ];

    private const string Project7 = """
        {"id":7,"name":"network","path_with_namespace":"modules/network",
         "description":"d","last_activity_at":"2026-01-01T00:00:00Z",
         "http_url_to_repo":"https://gitlab.example/modules/network.git"}
        """;

    /// <summary>A GitLab whose group 1 lists project 7, answering project 7 with one release whose outputs and variables files are absent, which is a supported case.</summary>
    private static StubHttpMessageHandler GitlabWithOneModule() =>
        new StubHttpMessageHandler()
            .AnswersJson("GET api/v4/groups/1/projects", $"[{Project7}]")
            .AnswersJson("GET api/v4/projects/7", Project7)
            .AnswersJson("GET api/v4/projects/7/releases", """[{"tag_name":"v1.0.0","released_at":"2026-01-01T00:00:00Z"}]""")
            .Answers("GET api/v4/projects/7/repository/files/*", HttpStatusCode.NotFound)
            .RefusesUnmatched();

    private static ServiceProvider Services(StubHttpMessageHandler handler, string token)
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

        return services.BuildServiceProvider();
    }

    /// <summary>The service over the test's own database; <c>GetModuleAsync</c> maps nothing, so it takes no mapper.</summary>
    private GitlabRepositoryService Service(IServiceProvider provider) =>
        new(
            Db,
            null,
            provider.GetRequiredService<IOptionsMonitor<TerraformOptions>>(),
            provider.GetRequiredService<IHttpClientFactory>());
}
