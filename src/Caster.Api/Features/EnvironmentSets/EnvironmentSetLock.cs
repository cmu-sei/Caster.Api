// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.EnvironmentSets;

/// <summary>
/// Serializes every change that can make two sets on one project give the same name: entry
/// edits, assignments and multi-edits. Each reads, checks the final state and commits under one
/// PostgreSQL transaction-scoped advisory lock, which holds across API instances, unlike the
/// process-local LockService.
/// </summary>
public static class EnvironmentSetLock
{
    /// <summary>An arbitrary key, the same for every such change.</summary>
    private const long Key = 0x4341_5354_4552_5345; // "CASTERSE"

    /// <summary>Runs <paramref name="work"/> in a transaction holding the lock, and commits it.</summary>
    public static async Task<T> WithEnvironmentSetLock<T>(this CasterContext dbContext, Func<Task<T>> work, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            await Acquire(dbContext, cancellationToken);
            return await work();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await Acquire(dbContext, cancellationToken);

        var result = await work();

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static Task Acquire(CasterContext dbContext, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [Key], cancellationToken);
}
