// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Services;
using Caster.Api.Domain.Services.Modules;
using Caster.Api.Infrastructure.Options;
using NSubstitute;
using Xunit;

namespace Caster.Api.Tests.Domain.Services.Modules
{
    [Trait("Category", "Unit")]
    [Trait("Category", "ModuleSources")]
    public class GitlabModuleRepositoryProviderTests
    {
        private readonly IGitlabRepositoryService _gitlab = Substitute.For<IGitlabRepositoryService>();

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        private GitlabModuleRepositoryProvider Sut => new(_gitlab, new RecordingLogger<GitlabModuleRepositoryProvider>());

        private static ModuleSourceOptions Source(string url) => new()
        {
            Name = ModuleRepositoryDispatcher.GitlabSourceName,
            Provider = ModuleSourceProviders.Gitlab,
            Url = url,
        };

        [Fact]
        public async Task AnApiUrlWithCredentials_IsRefusedBeforeGitlabIsCalled()
        {
            var source = Source("https://user:SECRET@gitlab.example.com/api/v4/");

            var exception = await Assert.ThrowsAsync<ModuleSourceRefusedException>(() => Sut.SyncModulesAsync(source, false, Ct));
            await Assert.ThrowsAsync<ModuleSourceRefusedException>(() => Sut.SyncModuleAsync(source, "11", Ct));

            Assert.Equal(ModuleSourceUrl.CredentialsMessage, exception.Message);
            await _gitlab.DidNotReceive().GetModulesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>());
            await _gitlab.DidNotReceive().GetModuleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task APlainApiUrl_Syncs()
        {
            var result = await Sut.SyncModulesAsync(Source("https://gitlab.example.com/api/v4/"), false, Ct);

            Assert.Equal(ModuleSourceState.Ok, result.State);
            await _gitlab.Received(1).GetModulesAsync(false, Arg.Any<CancellationToken>());
        }
    }
}
