// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Domain.Models;
using Xunit;

namespace Caster.Api.Tests.Domain.Models.Modules
{
    [Trait("Category", "Unit")]
    [Trait("Category", "Modules")]
    public class PlaceholdersTests
    {
        [Theory]
        [InlineData("REPLACE_WITH_DC_TEMPLATE", true)]
        [InlineData("${module.network.network_names[\"REPLACE_WITH_SEGMENT\"]}", true)]
        [InlineData("dc-template", false)]
        [InlineData("NOT_REPLACE_WITH_THIS", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsPlaceholder_Finds_One_Anywhere_In_The_Value(string value, bool expected)
        {
            Assert.Equal(expected, Placeholders.IsPlaceholder(value));
        }

        [Fact]
        public void For_Names_The_Variable_In_Capitals()
        {
            Assert.Equal("REPLACE_WITH_TEMPLATE_NAME", Placeholders.For("template_name"));
            Assert.Equal("REPLACE_WITH_SDN_ZONE", Placeholders.For("sdn-zone"));
        }

        [Fact]
        public void In_Lists_The_Variables_Still_To_Fill()
        {
            var values = new[]
            {
                new ModuleValue { Name = "name", Value = "dc01" },
                new ModuleValue { Name = "template_name", Value = "REPLACE_WITH_DC_TEMPLATE" },
                new ModuleValue { Name = "node_name", Value = "REPLACE_WITH_NODE_NAME" },
            };

            Assert.Equal(["template_name", "node_name"], Placeholders.In(values));
            Assert.Empty(Placeholders.In(null));
        }
    }
}
