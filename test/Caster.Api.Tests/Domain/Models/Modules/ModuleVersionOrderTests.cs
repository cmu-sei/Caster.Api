// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Caster.Api.Domain.Models;
using Caster.Api.Tests.Support;
using Xunit;

namespace Caster.Api.Tests.Domain.Models.Modules
{
    [Trait("Category", "Unit")]
    [Trait("Category", "Modules")]
    public class ModuleVersionOrderTests
    {
        /// <summary>A version of its own module, imported at <paramref name="created"/>.</summary>
        private static ModuleVersion Version(string name, DateTime created)
        {
            var version = TestData.ModuleVersion(TestData.Module(), name);
            version.DateCreated = created;

            return version;
        }

        [Fact]
        public void HighestFirst_Orders_By_Version_Not_Import_Date()
        {
            var now = DateTime.UtcNow;
            var versions = new[]
            {
                Version("v1.10.0", now.AddDays(-3)),
                Version("main", now),
                Version("v1.2.0", now.AddDays(-1)),
                Version("v2.0.0-rc1", now.AddDays(-2)),
                Version("v2.0.0", now.AddDays(-5)),
                Version("1.9", now.AddDays(-4)),
            };

            var names = ModuleVersionOrder.HighestFirst(versions).Select(v => v.Name);

            Assert.Equal(["v2.0.0", "v2.0.0-rc1", "v1.10.0", "1.9", "v1.2.0", "main"], names);
        }

        [Fact]
        public void HighestFirst_Orders_Per_Module_Tags_By_Their_Version()
        {
            var now = DateTime.UtcNow;
            var versions = new[]
            {
                Version("network-segment/v1.2.0", now),
                Version("v1.5.0", now),
                Version("network-segment/v1.10.0", now.AddDays(-1)),
            };

            var names = ModuleVersionOrder.HighestFirst(versions).Select(v => v.Name);

            Assert.Equal(["network-segment/v1.10.0", "v1.5.0", "network-segment/v1.2.0"], names);
        }

        [Fact]
        public void HighestFirst_Orders_Unparsable_Tags_Newest_First()
        {
            var now = DateTime.UtcNow;
            var versions = new[] { Version("main", now.AddDays(-1)), Version("develop", now) };

            var names = ModuleVersionOrder.HighestFirst(versions).Select(v => v.Name);

            Assert.Equal(["develop", "main"], names);
        }
    }
}
