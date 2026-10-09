// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Domain.Services.Modules;
using Xunit;

namespace Caster.Api.Tests.Unit.Modules
{
    [Trait("Category", "Unit")]
    [Trait("Category", "ModuleSources")]
    public class GitTagParserTests
    {
        [Fact]
        public void Parse_LightweightTag_ReturnsName()
        {
            var tags = GitTagParser.Parse(["bfac80998724f4c42e1ef18b2c7ac5fc1cf17a4c\trefs/tags/v0.1.0"]);

            Assert.Equal(["v0.1.0"], tags);
        }

        [Fact]
        public void Parse_AnnotatedTag_CollapsesPeeledRefToOneName()
        {
            // An annotated tag shows up twice: the tag object, then the commit
            // it peels to.
            var tags = GitTagParser.Parse(
            [
                "1111111111111111111111111111111111111111\trefs/tags/v1.0.0",
                "2222222222222222222222222222222222222222\trefs/tags/v1.0.0^{}",
            ]);

            Assert.Equal(["v1.0.0"], tags);
        }

        [Fact]
        public void Parse_MixedAnnotatedAndLightweight_ReturnsBoth()
        {
            var tags = GitTagParser.Parse(
            [
                "1111111111111111111111111111111111111111\trefs/tags/v1.0.0",
                "2222222222222222222222222222222222222222\trefs/tags/v1.0.0^{}",
                "3333333333333333333333333333333333333333\trefs/tags/v1.1.0",
            ]);

            Assert.Equal(["v1.0.0", "v1.1.0"], tags);
        }

        [Fact]
        public void Parse_NonTagRefs_AreIgnored()
        {
            var tags = GitTagParser.Parse(
            [
                "1111111111111111111111111111111111111111\trefs/heads/main",
                "2222222222222222222222222222222222222222\tHEAD",
                "3333333333333333333333333333333333333333\trefs/tags/v2.0.0",
            ]);

            Assert.Equal(["v2.0.0"], tags);
        }

        [Fact]
        public void Parse_BlankAndNullInput_ReturnsEmpty()
        {
            Assert.Empty(GitTagParser.Parse(null));
            Assert.Empty(GitTagParser.Parse(["", "   ", "\t"]));
        }

        [Fact]
        public void Parse_SpaceSeparatedLine_StillParses()
        {
            var tags = GitTagParser.Parse(["1111111111111111111111111111111111111111 refs/tags/v3.0.0"]);

            Assert.Equal(["v3.0.0"], tags);
        }

        [Fact]
        public void Parse_TagsWithSlashes_KeepTheirFullName()
        {
            var tags = GitTagParser.Parse(["1111111111111111111111111111111111111111\trefs/tags/release/2024-01"]);

            Assert.Equal(["release/2024-01"], tags);
        }

        [Fact]
        public void Parse_ReturnsStableOrdering_RegardlessOfRemoteOrder()
        {
            string[] forward =
            [
                "1111111111111111111111111111111111111111\trefs/tags/v0.1.0",
                "2222222222222222222222222222222222222222\trefs/tags/v0.2.0",
            ];

            string[] reversed =
            [
                "2222222222222222222222222222222222222222\trefs/tags/v0.2.0",
                "1111111111111111111111111111111111111111\trefs/tags/v0.1.0",
            ];

            Assert.Equal(GitTagParser.Parse(forward), GitTagParser.Parse(reversed));
        }
    }
}
