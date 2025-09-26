// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Caster.Api.Infrastructure.Utilities;

public class BackoffTracker
{
    private int _attempt;
    private int _maxDelay;

    public BackoffTracker(int maxDelaySeconds = 60) => _maxDelay = maxDelaySeconds;

    public Task WaitAsync(CancellationToken cancellationToken = default)
    {
        int exponent = Math.Min(_attempt, 30);
        int baseDelay = Math.Min(1 << exponent, _maxDelay);

        int min = Math.Max(1, (int)(baseDelay * 0.75));
        int max = Math.Min(_maxDelay, (int)(baseDelay * 1.25));

        int jitteredDelay = Random.Shared.Next(min, max + 1);

        _attempt++;
        return Task.Delay(TimeSpan.FromSeconds(jitteredDelay), cancellationToken);
    }

    public void Reset() => _attempt = 0;
}
