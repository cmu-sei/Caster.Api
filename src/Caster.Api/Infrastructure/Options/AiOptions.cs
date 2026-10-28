// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Caster.Api.Infrastructure.Options
{
    /// <summary>
    /// Settings for drafting Designs with a model on Amazon Bedrock.
    /// Credentials come from the default AWS chain (environment, profile or instance role).
    /// </summary>
    public class AiOptions
    {
        public bool Enabled { get; set; }
        public string Region { get; set; } = "us-east-1";
        public string ModelId { get; set; }
        // tried once when ModelId's safety classifiers decline a request; empty for no retry
        public string FallbackModelId { get; set; }
        // answers are streamed, so this can be well past what one unstreamed request finishes in
        public int MaxTokens { get; set; } = 64000;
        public int TimeoutSeconds { get; set; } = 600;
    }
}
