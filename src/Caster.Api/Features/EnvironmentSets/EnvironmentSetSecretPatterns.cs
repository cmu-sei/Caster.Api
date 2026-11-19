// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.EnvironmentSets;
using Caster.Api.Infrastructure.Exceptions;

namespace Caster.Api.Features.EnvironmentSets;

public static class EnvironmentSetSecretPatterns
{
    /// <summary>
    /// Refuses to assign <paramref name="set"/>, to a project or a group, if any of its secret entries
    /// has a source that no longer matches the secret patterns: they may have changed since the
    /// entries were saved. <paramref name="set"/> must be loaded with its entries.
    /// </summary>
    public static void EnsureAssignable(this ISecretSource secretSource, Domain.Models.EnvironmentSet set)
    {
        var disallowed = set.Entries
            .Where(x => x.ValueSource == EnvironmentSetValueSource.Secret && !secretSource.IsAllowed(x.SecretName))
            .Select(EnvironmentSetRules.Describe)
            .ToList();

        if (disallowed.Count > 0)
        {
            throw new ConflictException(
                $"Environment Set \"{set.Name}\" has secrets whose sources no longer match the secret patterns: {string.Join(", ", disallowed)}.");
        }
    }
}
