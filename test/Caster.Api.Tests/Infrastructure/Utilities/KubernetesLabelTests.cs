// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Text.RegularExpressions;
using Caster.Api.Infrastructure.Utilities;
using Xunit;

namespace Caster.Api.Tests.Infrastructure.Utilities
{
    [Trait("Category", "Unit")]
    public class KubernetesLabelTests
    {
        // https://kubernetes.io/docs/concepts/overview/working-with-objects/labels/#syntax-and-character-set
        private static readonly Regex ValidLabelValue = new("^(([A-Za-z0-9][-A-Za-z0-9_.]*)?[A-Za-z0-9])?$");

        [Theory]
        [InlineData("ok.name_1-2", "ok.name_1-2")]
        [InlineData("bad name!!", "bad_name")]
        [InlineData("café", "caf")]
        [InlineData("-leading-and-trailing-", "leading-and-trailing")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void Test_ToValue_Produces_Expected_Value(string input, string expected)
        {
            Assert.Equal(expected, KubernetesLabel.ToValue(input));
        }

        [Theory]
        [InlineData(90)]
        [InlineData(64)]
        [InlineData(63)]
        public void Test_ToValue_Limits_Length(int length)
        {
            var value = KubernetesLabel.ToValue(new string('a', length));

            Assert.True(value.Length <= 63);
        }

        [Theory]
        [InlineData("a_very_long_display_name_for_someone-6fe1b3d2-0d7c-4c1e-9d5a-2f3b8c9e4a10")]
        [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa_b")]
        [InlineData("...")]
        [InlineData("Smith, John + 🙂")]
        public void Test_ToValue_Is_A_Valid_Label_Value(string input)
        {
            Assert.Matches(ValidLabelValue, KubernetesLabel.ToValue(input));
        }
    }
}
