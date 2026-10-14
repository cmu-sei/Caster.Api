// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Caster.Api.Domain.Models;
using Xunit;

namespace Caster.Api.Tests.Unit.Modules
{
    [Trait("Category", "Unit")]
    [Trait("Category", "Modules")]
    public class ModuleVersionSnippetTests
    {
        private static ModuleVersion Version(string name, DateTime? created = null) => new()
        {
            Name = name,
            UrlLink = "https://example.com/modules/vm.git",
            DateCreated = created ?? DateTime.UtcNow,
            Variables =
            [
                new ModuleVariable { Name = "vm_name", VariableType = "string" },
                new ModuleVariable { Name = "cpus", VariableType = "number", DefaultValue = "2" },
            ]
        };

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
