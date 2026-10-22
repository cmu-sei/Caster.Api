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
    public class ModuleVersionRequiredTests
    {
        /// <summary>A version whose <c>vm_name</c> is required and whose <c>cpus</c> defaults to 2.</summary>
        private static ModuleVersion Version(string name)
        {
            var version = TestData.ModuleVersion(TestData.Module(), name);
            version.Variables =
            [
                new ModuleVariable { Name = "vm_name", VariableType = "string" },
                new ModuleVariable { Name = "cpus", VariableType = "number", DefaultValue = "2" },
            ];

            return version;
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void MissingRequiredVariables_Reports_Unset_Required(string value)
        {
            var values = value == null
                ? new List<ModuleValue>()
                : [new ModuleValue { Name = "vm_name", Value = value }];

            Assert.Equal(["vm_name"], Version("v1.0.0").MissingRequiredVariables(values));
        }

        [Fact]
        public void MissingRequiredVariables_Accepts_Variable_Reference()
        {
            var values = new List<ModuleValue> { new() { Name = "vm_name", Value = "${var.name}" } };

            Assert.Empty(Version("v1.0.0").MissingRequiredVariables(values));
        }
    }
}
