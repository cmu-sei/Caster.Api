// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Domain.Services.Modules;
using Xunit;

namespace Caster.Api.Tests.Domain.Services.Modules
{
    [Trait("Category", "Unit")]
    [Trait("Category", "ModuleSources")]
    public class ModuleSourceReadCacheTests
    {
        [Fact]
        public void SourceNames_IgnoreCase()
        {
            var cache = new ModuleSourceReadCache();
            cache.RecordEmpty("catalog", "f", EmptyReadOutcome.TaggedNoModuleRoots);
            cache.RecordHasBranches("catalog", "r", true);

            Assert.True(cache.TryGetEmpty("Catalog", "f", out _));
            Assert.True(cache.TryGetHasBranches("CATALOG", "r", out var hasBranches));
            Assert.True(hasBranches);
        }

        [Fact]
        public void ADifferentFingerprint_IsAMiss()
        {
            var cache = new ModuleSourceReadCache();
            cache.RecordEmpty("catalog", "f", EmptyReadOutcome.TaggedNoModuleRoots);
            cache.RecordHasBranches("catalog", "r", true);

            Assert.False(cache.TryGetEmpty("catalog", "g", out _));
            Assert.False(cache.TryGetHasBranches("catalog", "s", out _));
        }

        [Fact]
        public void Forget_DropsTheEmptyRead()
        {
            var cache = new ModuleSourceReadCache();
            cache.RecordEmpty("catalog", "f", EmptyReadOutcome.TaggedNoModuleRoots);

            cache.Forget("catalog");

            Assert.False(cache.TryGetEmpty("catalog", "f", out _));
        }
    }
}
