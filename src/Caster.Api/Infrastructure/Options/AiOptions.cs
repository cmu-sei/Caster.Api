// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Caster.Api.Infrastructure.Options
{
    /// <summary>
    /// Settings for drafting Designs and Terraform with a model: on Amazon Bedrock by default, whose credentials come from the
    /// default AWS chain (environment, profile or instance role), or behind an OpenAI-compatible endpoint for a task that names one.
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
        // the model each task uses where it is not the default above, keyed by task: design, terraform, brief (see AiTasks).
        // Any field left empty is the default's.
        public Dictionary<string, AiTaskModel> Models { get; set; } = new Dictionary<string, AiTaskModel>();
        // how hard a model thinks before it answers: low, medium, high, xhigh or max; empty for the model's own default.
        // Lower is faster and spends fewer tokens; a task's own Effort wins.
        public string Effort { get; set; }
        // what each model costs, keyed by model or inference profile id, so the AI usage of a project can be priced;
        // a model without a price is counted in tokens only. A key matches ignoring punctuation, so an environment
        // variable can price us.anthropic.claude-sonnet-5-5 as us_anthropic_claude_sonnet_5_5.
        public Dictionary<string, AiPrice> Prices { get; set; } = new Dictionary<string, AiPrice>();

        public const string Bedrock = "Bedrock";
        public const string OpenAi = "OpenAI";

        /// <summary>The provider and model a task's calls go to: its own settings where it has them, the defaults where not</summary>
        public AiTaskModel ModelFor(string task)
        {
            var own = task == null ? null : Models?.FirstOrDefault(m => string.Equals(m.Key, task, StringComparison.OrdinalIgnoreCase)).Value;
            var provider = string.IsNullOrWhiteSpace(own?.Provider) ? Bedrock : own.Provider.Trim();
            var bedrock = string.Equals(provider, Bedrock, StringComparison.OrdinalIgnoreCase);
            return new AiTaskModel
            {
                Provider = bedrock ? Bedrock : provider,
                // another provider's models are not Bedrock's, so it takes nothing of the default but the limits
                ModelId = Pick(own?.ModelId, bedrock ? ModelId : null),
                FallbackModelId = Pick(own?.FallbackModelId, bedrock ? FallbackModelId : null),
                Region = Pick(own?.Region, Region),
                BaseUrl = own?.BaseUrl,
                ApiKey = own?.ApiKey,
                MaxTokens = own?.MaxTokens > 0 ? own.MaxTokens : MaxTokens,
                Effort = Pick(own?.Effort, Effort)
            };

            static string Pick(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }

    /// <summary>The AI tasks, by the names AiOptions.Models is keyed with</summary>
    public static class AiTasks
    {
        // a Design drafted from the module catalog
        public const string Design = "design";
        // Terraform drafted or changed in a Directory from a request in words
        public const string Terraform = "terraform";
        // Terraform drafted in a Directory from a structured scenario brief
        public const string Brief = "brief";
    }

    /// <summary>Where one task's model calls go</summary>
    public class AiTaskModel
    {
        // Bedrock, the default, or OpenAI for the OpenAI API and servers that offer it, such as vLLM and Ollama
        public string Provider { get; set; }
        public string ModelId { get; set; }
        public string FallbackModelId { get; set; }
        // Bedrock's region
        public string Region { get; set; }
        // an OpenAI-compatible endpoint, e.g. https://api.openai.com/v1; chat/completions is added to it
        public string BaseUrl { get; set; }
        public string ApiKey { get; set; }
        public int MaxTokens { get; set; }
        // low, medium, high, xhigh or max; empty for the default
        public string Effort { get; set; }
    }

    /// <summary>A model's price in US dollars per million tokens of each kind</summary>
    public class AiPrice
    {
        public decimal InputPerMillion { get; set; }
        public decimal OutputPerMillion { get; set; }
        public decimal CacheReadPerMillion { get; set; }
        public decimal CacheWritePerMillion { get; set; }
    }
}
