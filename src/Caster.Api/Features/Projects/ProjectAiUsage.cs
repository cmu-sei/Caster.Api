// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;

namespace Caster.Api.Features.Projects;

/// <summary>Calls to AI models and what they used</summary>
public class AiUsageTotals
{
    /// <summary>The project's name, or the task: design, terraform or brief</summary>
    public string Name { get; set; }

    public int Calls { get; set; }

    /// <summary>Calls a safety classifier declined; they are billed too</summary>
    public int Refused { get; set; }

    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheWriteTokens { get; set; }

    /// <summary>
    /// The estimated cost in millionths of a US dollar (1,000,000 is $1), from the prices in the Ai:Prices setting, of
    /// the calls whose models have a price; null when none has. A whole number, so no rounding is left to the client.
    /// </summary>
    public long? EstimatedCostMicros { get; set; }
}

/// <summary>What drafting with AI used in one project, in all and by task</summary>
public class ProjectAiUsage : AiUsageTotals
{
    public Guid ProjectId { get; set; }

    /// <summary>Models used that have no configured price, which the estimate leaves out</summary>
    public List<string> Unpriced { get; set; } = [];

    public DateTime? FirstCall { get; set; }
    public DateTime? LastCall { get; set; }
    public List<AiUsageTotals> ByTask { get; set; } = [];
}
