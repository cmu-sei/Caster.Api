// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Caster.Api.Domain.Services
{
    /// <summary>
    /// Writes one row per model call, against the Project of the Directory the request's <see cref="IAiTaskContext"/> names.
    /// It writes on a context of its own, so it never saves anything else the request has in hand.
    /// </summary>
    public interface IAiUsageRecorder
    {
        /// <summary>Records the usage a Messages API answer reports (an OpenAI answer is first put in the same terms)</summary>
        Task RecordAsync(string model, JsonElement message, CancellationToken ct);
    }

    public class AiUsageRecorder : IAiUsageRecorder
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IAiTaskContext _task;
        private readonly IIdentityResolver _identity;
        private readonly ILogger<AiUsageRecorder> _logger;

        public AiUsageRecorder(IServiceScopeFactory scopeFactory, IAiTaskContext task, IIdentityResolver identity, ILogger<AiUsageRecorder> logger)
        {
            _scopeFactory = scopeFactory;
            _task = task;
            _identity = identity;
            _logger = logger;
        }

        public async Task RecordAsync(string model, JsonElement message, CancellationToken ct)
        {
            var usage = Read(model, message);
            usage.Task = _task.Task ?? "other";
            usage.DirectoryId = _task.DirectoryId;
            usage.CreatedById = UserId();
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CasterContext>();
                if (usage.DirectoryId.HasValue)
                {
                    usage.ProjectId = await db.Directories
                        .Where(d => d.Id == usage.DirectoryId.Value)
                        .Select(d => (Guid?)d.ProjectId)
                        .SingleOrDefaultAsync(ct);
                }
                db.AiUsages.Add(usage);
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // the call already happened; losing its record must not lose the planner's draft
                _logger.LogWarning(ex, "Could not record the AI usage of a {Task} call on {Model}", usage.Task, model);
            }
        }

        // a call made with no signed-in user is recorded without one
        private Guid? UserId()
        {
            try
            {
                var id = _identity.GetClaimsPrincipal()?.GetId();
                return id == Guid.Empty ? null : id;
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                return null;
            }
        }

        /// <summary>The usage a Messages API answer reports: its token counts and why the model stopped</summary>
        public static AiUsage Read(string model, JsonElement message)
        {
            var usage = new AiUsage { Id = Guid.NewGuid(), Model = model, DateCreated = DateTime.UtcNow };
            if (message.ValueKind != JsonValueKind.Object)
                return usage;
            if (message.TryGetProperty("stop_reason", out var stop) && stop.ValueKind == JsonValueKind.String)
                usage.StopReason = stop.GetString();
            if (message.TryGetProperty("usage", out var counts) && counts.ValueKind == JsonValueKind.Object)
            {
                int Count(string name) => counts.TryGetProperty(name, out var value) && value.TryGetInt32(out var n) ? n : 0;
                usage.InputTokens = Count("input_tokens");
                usage.OutputTokens = Count("output_tokens");
                usage.CacheReadTokens = Count("cache_read_input_tokens");
                usage.CacheWriteTokens = Count("cache_creation_input_tokens");
            }
            return usage;
        }
    }
}
