// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;

namespace Caster.Api.Tests.Support;

/// <summary>
/// An API environment variable matching the shipped secret pattern (<c>^CASTER_SECRET_</c>), set on the
/// test process for the test's lifetime. The name is unique, so tests running at once never see each other's.
/// </summary>
public sealed class SecretEnvironmentVariable : IDisposable
{
    public SecretEnvironmentVariable(string value)
    {
        Name = $"CASTER_SECRET_TEST_{Guid.NewGuid():N}";
        Value = value;
        Environment.SetEnvironmentVariable(Name, value);
    }

    public string Name { get; }

    public string Value { get; }

    /// <summary>A name matching the pattern that is not set.</summary>
    public static string Missing() => $"CASTER_SECRET_TEST_MISSING_{Guid.NewGuid():N}";

    public void Dispose() => Environment.SetEnvironmentVariable(Name, null);
}
