// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Options;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.Projects;

public class GetAiUsage
{
    [DataContract(Name = "GetProjectAiUsageQuery")]
    public class Query : IRequest<ProjectAiUsage>
    {
        /// <summary>
        /// The Id of the Project
        /// </summary>
        [DataMember]
        public Guid Id { get; set; }
    }

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext, AiOptions aiOptions) : BaseHandler<Query, ProjectAiUsage>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Domain.Models.Project>(request.Id, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);

        public override async Task<ProjectAiUsage> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var project = await dbContext.Projects
                .AsNoTracking()
                .Where(p => p.Id == request.Id)
                .Select(p => new { p.Id, p.Name })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new EntityNotFoundException<Project>();

            var rows = await dbContext.AiUsages
                .AsNoTracking()
                .Where(u => u.ProjectId == request.Id)
                .ToListAsync(cancellationToken);

            return Summarize(project.Id, project.Name, rows, aiOptions.Prices ?? new Dictionary<string, AiPrice>());
        }
    }

    /// <summary>
    /// A model's price. Environment variable names can't hold a model id's dots or hyphens, so a price keyed
    /// us_anthropic_claude_sonnet_5_5 is the price of us.anthropic.claude-sonnet-5-5.
    /// </summary>
    public static AiPrice PriceFor(IReadOnlyDictionary<string, AiPrice> prices, string model)
    {
        if (string.IsNullOrEmpty(model) || prices == null)
            return null;
        if (prices.TryGetValue(model, out var price))
            return price;
        static string Letters(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray());
        var key = Letters(model);
        return prices.FirstOrDefault(p => Letters(p.Key).Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
    }

    /// <summary>Totals for a project, by task, with a cost estimate of the calls whose models have a price</summary>
    public static ProjectAiUsage Summarize(Guid projectId, string projectName, List<AiUsage> rows, IReadOnlyDictionary<string, AiPrice> prices)
    {
        long? Cost(IEnumerable<AiUsage> calls)
        {
            decimal total = 0;
            var priced = false;
            foreach (var call in calls)
            {
                // what has no price is left out of the estimate, and named in Unpriced
                if (PriceFor(prices, call.Model) is not AiPrice price)
                    continue;
                priced = true;
                total += call.InputTokens * price.InputPerMillion / 1_000_000m
                    + call.OutputTokens * price.OutputPerMillion / 1_000_000m
                    + call.CacheReadTokens * price.CacheReadPerMillion / 1_000_000m
                    + call.CacheWriteTokens * price.CacheWritePerMillion / 1_000_000m;
            }
            return priced ? (long)Math.Round(total * 1_000_000m) : null;
        }

        AiUsageTotals Totals(string name, List<AiUsage> calls) => new()
        {
            Name = name,
            Calls = calls.Count,
            Refused = calls.Count(c => c.StopReason == "refusal"),
            InputTokens = calls.Sum(c => (long)c.InputTokens),
            OutputTokens = calls.Sum(c => (long)c.OutputTokens),
            CacheReadTokens = calls.Sum(c => (long)c.CacheReadTokens),
            CacheWriteTokens = calls.Sum(c => (long)c.CacheWriteTokens),
            EstimatedCostMicros = Cost(calls)
        };

        var all = Totals(projectName, rows);
        return new ProjectAiUsage
        {
            ProjectId = projectId,
            Name = projectName,
            Calls = all.Calls,
            Refused = all.Refused,
            InputTokens = all.InputTokens,
            OutputTokens = all.OutputTokens,
            CacheReadTokens = all.CacheReadTokens,
            CacheWriteTokens = all.CacheWriteTokens,
            EstimatedCostMicros = all.EstimatedCostMicros,
            Unpriced = rows.Select(r => r.Model).Where(m => m != null && PriceFor(prices, m) == null).Distinct().Order().ToList(),
            FirstCall = rows.Count > 0 ? rows.Min(r => r.DateCreated) : null,
            LastCall = rows.Count > 0 ? rows.Max(r => r.DateCreated) : null,
            ByTask = rows.GroupBy(r => r.Task ?? "other").OrderBy(g => g.Key).Select(g => Totals(g.Key, g.ToList())).ToList()
        };
    }
}
