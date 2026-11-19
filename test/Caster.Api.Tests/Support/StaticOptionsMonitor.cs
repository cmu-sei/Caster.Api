// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.Extensions.Options;

namespace Caster.Api.Tests.Support;

/// <summary>An <see cref="IOptionsMonitor{T}"/> that always answers one value, for a unit built without a host.</summary>
public sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string name) => value;

    public IDisposable OnChange(Action<T, string> listener) => null;
}
