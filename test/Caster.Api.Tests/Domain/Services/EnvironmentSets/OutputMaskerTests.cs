// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Domain.Services.EnvironmentSets;

namespace Caster.Api.Tests.Domain.Services.EnvironmentSets;

[Trait("Category", "Unit")]
public class OutputMaskerTests
{
    [Fact]
    public void A_secret_of_eight_characters_or_more_is_masked_wherever_it_appears()
    {
        var masker = new OutputMasker(["s3cr3t-value"]);

        Assert.Equal("Error: auth failed for *** (*** rejected)", masker.Apply("Error: auth failed for s3cr3t-value (s3cr3t-value rejected)"));
    }

    [Fact]
    public void A_secret_shorter_than_eight_characters_is_left_alone()
    {
        var masker = new OutputMasker(["short"]);

        Assert.True(masker.IsEmpty);
        Assert.Equal("a short line", masker.Apply("a short line"));
    }

    [Fact]
    public void A_secret_containing_another_is_masked_whole()
    {
        var masker = new OutputMasker(["password", "password-and-more"]);

        Assert.Equal("x *** y", masker.Apply("x password-and-more y"));
    }

    [Fact]
    public void Each_line_of_a_multi_line_secret_is_masked_on_its_own()
    {
        var masker = new OutputMasker(["-----BEGIN KEY-----\r\nMIIEpAIBAAKCAQEA\n-----END KEY-----"]);

        Assert.Equal("***", masker.Apply("MIIEpAIBAAKCAQEA"));
    }

    [Fact]
    public void Null_and_empty_output_pass_through()
    {
        var masker = new OutputMasker(["s3cr3t-value"]);

        Assert.Null(masker.Apply(null));
        Assert.Equal(string.Empty, masker.Apply(string.Empty));
    }
}
