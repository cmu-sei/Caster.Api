// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Text;
using Caster.Api.Domain.Models;
using Xunit;

namespace Caster.Api.Tests.Unit.Serialization
{
    /// <summary>
    /// A Terraform module variable's default is read into a string, whatever JSON type it
    /// had. The value is written straight into generated HCL, so the spelling has to be
    /// what Terraform accepts -- lower-case for booleans. See CRU-2054.
    /// </summary>
    [Trait("Category", "Unit")]
    [Trait("Category", "Serialization")]
    public class NumberToStringConverterTests
    {
        private static string DefaultOf(string variablesJson, string name) =>
            GitlabModuleVariableResponse
                .GetModuleVariables(Encoding.UTF8.GetBytes(variablesJson))
                .Single(x => x.Name == name)
                .DefaultValue;

        [Theory]
        [InlineData("true", "true")]
        [InlineData("false", "false")]
        public void Test_Boolean_Default_Keeps_Terraform_Casing(string json, string expected)
        {
            var variables = $@"{{ ""variable"": {{ ""flag"": {{ ""type"": ""bool"", ""default"": {json} }} }} }}";

            Assert.Equal(expected, DefaultOf(variables, "flag"));
        }

        [Theory]
        [InlineData("42", "42")]
        [InlineData("1.5", "1.5")]
        [InlineData("\"a string\"", "a string")]
        public void Test_Other_Default_Types_Are_Unchanged(string json, string expected)
        {
            var variables = $@"{{ ""variable"": {{ ""v"": {{ ""default"": {json} }} }} }}";

            Assert.Equal(expected, DefaultOf(variables, "v"));
        }

        [Fact]
        public void Test_List_Default_Still_Falls_Through_To_Json()
        {
            // Not a boolean, number or string: the JsonDocument fallback still applies,
            // so a composite default keeps its JSON spelling.
            var variables = @"{ ""variable"": { ""v"": { ""type"": ""list(string)"", ""default"": [""a"",""b""] } } }";

            Assert.Equal(@"[""a"",""b""]", DefaultOf(variables, "v"));
        }
    }
}
