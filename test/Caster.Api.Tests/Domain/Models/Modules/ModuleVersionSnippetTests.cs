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
    public class ModuleVersionSnippetTests
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

        [Fact]
        public void ToSnippet_Comments_Out_Blank_Required_Value()
        {
            var snippet = Version("v1.0.0").ToSnippet("vm", [
                new ModuleValue { Name = "vm_name", Value = "" },
                new ModuleValue { Name = "cpus", Value = "4" },
            ]);

            Assert.Contains("# vm_name = (required)", snippet);
            Assert.DoesNotContain("vm_name = \"\"", snippet);
            Assert.Contains("cpus = 4", snippet);
        }

        [Fact]
        public void ToSnippet_Leaves_Out_Blank_Optional_Number()
        {
            var snippet = Version("v1.0.0").ToSnippet("vm", [
                new ModuleValue { Name = "vm_name", Value = "web" },
                new ModuleValue { Name = "cpus", Value = "" },
            ]);

            Assert.DoesNotContain("cpus", snippet);
        }

        [Fact]
        public void ToSnippet_Quotes_Set_String_Value()
        {
            var snippet = Version("v1.0.0").ToSnippet("vm", [new ModuleValue { Name = "vm_name", Value = "web" }]);

            Assert.Contains("vm_name = \"web\"", snippet);
        }
    }
}
