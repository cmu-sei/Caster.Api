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
            cache.RecordEmpty("catalog", "f", EmptyRead.Of(EmptyReadOutcome.TaggedNoModuleRoots));

            Assert.True(cache.TryGetEmpty("Catalog", "f", out _));
            Assert.True(cache.NeedsBackfill("catalog"));
            cache.MarkBackfilled("catalog");
            Assert.False(cache.NeedsBackfill("CATALOG"));
        }

        [Fact]
        public void ADifferentFingerprint_IsAMiss()
        {
            var cache = new ModuleSourceReadCache();
            cache.RecordEmpty("catalog", "f", EmptyRead.Of(EmptyReadOutcome.TaggedNoModuleRoots));

            Assert.False(cache.TryGetEmpty("catalog", "g", out _));
        }

        [Fact]
        public void AReadThatStoredModules_IsNoEmptyHitButIsTheLastRead()
        {
            var cache = new ModuleSourceReadCache();
            cache.RecordStored("catalog", "f");

            Assert.False(cache.TryGetEmpty("catalog", "f", out _));
            Assert.True(cache.LastReadWas("catalog", "f"));
            Assert.False(cache.LastReadWas("catalog", "g"));
        }
    }
}
