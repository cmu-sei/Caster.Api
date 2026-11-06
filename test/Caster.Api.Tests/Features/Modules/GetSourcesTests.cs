// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading.Tasks;
using Caster.Api.Domain.Services.Modules;
using Caster.Api.Features.Modules;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Options;
using NSubstitute;
using Xunit;

namespace Caster.Api.Tests.Features.Modules
{
    /// <summary><c>GetSources.Handler</c> over a substitute module source seam, for states the HTTP host cannot set up.</summary>
    [Trait("Category", "Unit")]
    [Trait("Category", "ModuleSources")]
    public class GetSourcesTests
    {
        [Fact]
        public async Task ASourceNeverRecorded_HasNoState()
        {
            var modules = Substitute.For<IModuleRepositoryService>();
            modules.GetSourceStatuses().Returns([
                (new ModuleSourceOptions { Name = "catalog", Provider = ModuleSourceProviders.Git, Url = "file:///srv/git/catalog.git" },
                    (ModuleSourceStatus)null),
            ]);
            var handler = new GetSources.Handler(Substitute.For<ICasterAuthorizationService>(), modules);

            var source = Assert.Single(await handler.HandleRequest(new GetSources.Query(), TestContext.Current.CancellationToken));

            Assert.Null(source.State);
            Assert.Null(source.Message);
            Assert.Null(source.LastAttempt);
        }

        [Theory]
        [InlineData(ModuleSourceProviders.Git, "https://oauth2:SECRET@git.example.com/org/catalog.git")]
        [InlineData(ModuleSourceProviders.Git, "ghp_SECRET@github.com:org/catalog.git")]
        [InlineData(ModuleSourceProviders.Gitlab, "https://user:SECRET@gitlab.example.com/api/v4/")]
        public async Task ARefusedSource_HasNoUrlAndItsErrorNamesTheProblem(string provider, string url)
        {
            var statuses = new ModuleSourceStatusStore();
            statuses.RecordFailure("catalog", ModuleSourceUrl.CredentialsMessage);
            var modules = Substitute.For<IModuleRepositoryService>();
            modules.GetSourceStatuses().Returns([
                (new ModuleSourceOptions { Name = "catalog", Provider = provider, Url = url }, statuses.Get("catalog")),
            ]);
            var handler = new GetSources.Handler(Substitute.For<ICasterAuthorizationService>(), modules);

            var source = Assert.Single(await handler.HandleRequest(new GetSources.Query(), TestContext.Current.CancellationToken));

            Assert.Null(source.Url);
            Assert.Equal(ModuleSourceState.Error, source.State);
            Assert.Equal(ModuleSourceUrl.CredentialsMessage, source.Message);
        }
    }
}
