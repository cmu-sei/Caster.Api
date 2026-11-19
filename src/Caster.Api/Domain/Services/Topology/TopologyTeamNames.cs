// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Caster.Api.Domain.Models;

namespace Caster.Api.Domain.Services.Topology;

/// <summary>Names a topology's console teams that it has only ids for.</summary>
public static partial class TopologyTeamNames
{
    /// <summary>Gives each unnamed team the name of the first of <paramref name="named"/> with its id.</summary>
    public static void Fill(Models.Topology topology, IEnumerable<TopologyTeam> named)
    {
        var byId = named
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        if (byId.Count == 0)
        {
            return;
        }

        foreach (var console in topology.Nodes.Select(x => x.Console).Where(x => x != null))
        {
            console.Teams = [.. console.Teams.Select(x => x.Name == null ? byId.GetValueOrDefault(x.Id) ?? x : x)];
        }
    }

    /// <summary>
    /// Teams named by a <c>.tfvars</c> line that sets a variable to an id, as
    /// Alloy writes its view's teams. Only id values are read.
    /// </summary>
    public static IEnumerable<TopologyTeam> FromTfvars(IEnumerable<string> contents) =>
        contents
            .Where(x => x != null)
            .SelectMany(x => IdAssignment().Matches(x))
            .Select(x => new TopologyTeam { Id = x.Groups["id"].Value, Name = x.Groups["name"].Value, Variable = x.Groups["name"].Value });

    [GeneratedRegex("""^[ \t]*(?<name>[A-Za-z_][A-Za-z0-9_-]*)[ \t]*=[ \t]*"(?<id>[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})"[ \t]*\r?$""", RegexOptions.Multiline)]
    private static partial Regex IdAssignment();
}
