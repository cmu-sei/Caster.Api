// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Caster.Api.Domain.Services.Inventory;

/// <summary>A configured infrastructure connection, by the name configuration spells it.</summary>
public record InfrastructureConnection(string Name, int Order, InfrastructureConnectionOptions Options);

/// <summary>
/// The configured infrastructure connections, read from configuration as it is now. Names are
/// compared the way the inventory compares them: trimmed and ignoring case. A connection without
/// a name, or with a name already used, is left out, as the inventory leaves it out.
/// </summary>
public interface IInfrastructureConnectionCatalog
{
    /// <summary>Every connection, in configuration order.</summary>
    IReadOnlyList<InfrastructureConnection> GetAll();

    /// <summary>The connection called <paramref name="name"/>, or null.</summary>
    InfrastructureConnection Find(string name);
}

public class InfrastructureConnectionCatalog(IOptionsMonitor<InfrastructureOptions> options) : IInfrastructureConnectionCatalog
{
    public IReadOnlyList<InfrastructureConnection> GetAll()
    {
        var result = new List<InfrastructureConnection>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var configured = options.CurrentValue?.Connections ?? [];

        for (var i = 0; i < configured.Count; i++)
        {
            var name = configured[i]?.Name?.Trim();

            if (string.IsNullOrEmpty(name) || !seen.Add(name))
                continue;

            result.Add(new InfrastructureConnection(name, i, configured[i]));
        }

        return result;
    }

    public InfrastructureConnection Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var wanted = name.Trim();

        foreach (var connection in GetAll())
        {
            if (string.Equals(connection.Name, wanted, StringComparison.OrdinalIgnoreCase))
                return connection;
        }

        return null;
    }
}
