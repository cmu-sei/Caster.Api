// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;

namespace Caster.Api.Domain.Services.Modules;

/// <summary>What a repeated failure should do to the log.</summary>
public enum ModuleSourceFailureLogAction
{
    /// <summary>The first failure, or a different error from the last one: log it in full.</summary>
    Log,

    /// <summary>The same error again, after the backoff: remind the operator it is still failing.</summary>
    Remind,

    /// <summary>The same error again, inside the backoff: nothing new to say.</summary>
    Suppress,
}

/// <summary>
/// Decides when a module source's failure is worth a log line. Module sources sync on every Module list, so
/// a source that stays broken would otherwise repeat the same error many times an hour.
/// </summary>
/// <remarks>
/// The first failure and every change of error are logged. The same error is then silent until a reminder is
/// due, and each reminder waits twice as long as the one before, up to <see cref="MaxReminderInterval"/>.
/// A success clears the source, so its next failure is a first failure again. In memory and per API
/// instance, like <see cref="ModuleSourceStatusStore"/>. Keyed by source name; the caller never passes
/// anything else to log, so no credential can reach a log line through here.
/// </remarks>
public class ModuleSourceFailureLog(TimeProvider timeProvider)
{
    public static readonly TimeSpan FirstReminderInterval = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MaxReminderInterval = TimeSpan.FromHours(4);

    private readonly Dictionary<string, FailureState> _failing = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>How long a source has been failing, and how many failures that is, for the recovery line.</summary>
    public readonly record struct Recovery(TimeSpan FailingFor, int Failures);

    /// <summary>Records a failure and says how to log it. Failures logged earlier and skipped since come back as <paramref name="suppressed"/>.</summary>
    public ModuleSourceFailureLogAction RecordFailure(string sourceName, string error, out int suppressed)
    {
        var now = timeProvider.GetUtcNow();

        lock (_lock)
        {
            suppressed = 0;

            if (!_failing.TryGetValue(sourceName, out var state))
            {
                _failing[sourceName] = new FailureState(error, now, FirstReminderInterval);
                return ModuleSourceFailureLogAction.Log;
            }

            state.Failures++;

            if (!string.Equals(state.Error, error, StringComparison.Ordinal))
            {
                state.Error = error;
                state.Suppressed = 0;
                state.Interval = FirstReminderInterval;
                state.NextLog = now + FirstReminderInterval;
                return ModuleSourceFailureLogAction.Log;
            }

            if (now < state.NextLog)
            {
                state.Suppressed++;
                return ModuleSourceFailureLogAction.Suppress;
            }

            suppressed = state.Suppressed;
            state.Suppressed = 0;
            state.Interval = TimeSpan.FromTicks(Math.Min(state.Interval.Ticks * 2, MaxReminderInterval.Ticks));
            state.NextLog = now + state.Interval;
            return ModuleSourceFailureLogAction.Remind;
        }
    }

    /// <summary>Records a success. Returns true, with the outage, if the source had been failing.</summary>
    public bool RecordSuccess(string sourceName, out Recovery recovery)
    {
        lock (_lock)
        {
            if (!_failing.Remove(sourceName, out var state))
            {
                recovery = default;
                return false;
            }

            recovery = new Recovery(timeProvider.GetUtcNow() - state.Since, state.Failures);
            return true;
        }
    }

    private class FailureState
    {
        public FailureState(string error, DateTimeOffset since, TimeSpan interval)
        {
            Error = error;
            Since = since;
            Interval = interval;
            NextLog = since + interval;
        }

        public string Error { get; set; }
        public DateTimeOffset Since { get; }
        public TimeSpan Interval { get; set; }
        public DateTimeOffset NextLog { get; set; }
        public int Failures { get; set; } = 1;
        public int Suppressed { get; set; }
    }
}
