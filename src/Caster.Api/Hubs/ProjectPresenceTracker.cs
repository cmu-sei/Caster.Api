// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Caster.Api.Hubs;

/// <summary>A user with a project open, as the other users in the project see them.</summary>
public record PresentUser(Guid Id, string Name);

/// <summary>
/// Everyone with a project open, each user once, as of <see cref="Version"/>. Versions only increase, so a
/// client that has seen one list can tell that another is older and ignore it.
/// </summary>
public record PresenceState(Guid ProjectId, long Version, IReadOnlyList<PresentUser> Users);

/// <summary>
/// What a change to a project's presence left behind. <see cref="Changed"/> is true only when the set of
/// users changed, the only time the others need telling.
/// </summary>
public record PresenceChange(PresenceState State, bool Changed);

/// <summary>
/// Who has each project open, by SignalR connection, so that a user with the project open in several tabs
/// arrives with the first and departs with the last.
/// </summary>
/// <remarks>
/// In memory, like the hub groups it shadows: with more than one API instance each knows only the
/// connections made to it. Each change returns the state it produced, so the caller can send it after the
/// tracker has let go of its lock; the version is what keeps states that cross on the way in order.
/// </remarks>
public interface IProjectPresenceTracker
{
    /// <summary>Records <paramref name="connectionId"/> as having <paramref name="projectId"/> open.</summary>
    PresenceChange Join(Guid projectId, string connectionId, PresentUser user);

    /// <summary>Forgets that <paramref name="connectionId"/> has <paramref name="projectId"/> open.</summary>
    /// <returns>The result, or null when the connection did not have the project open.</returns>
    PresenceChange Leave(Guid projectId, string connectionId);

    /// <summary>Forgets every project <paramref name="connectionId"/> had open, for a disconnect.</summary>
    /// <returns>The result for each project the connection had open.</returns>
    IReadOnlyList<PresenceChange> LeaveAll(string connectionId);

    /// <summary>The projects <paramref name="connectionId"/> has open.</summary>
    IReadOnlyList<Guid> ProjectsOf(string connectionId);
}

public class ProjectPresenceTracker : IProjectPresenceTracker
{
    private readonly object _lock = new();

    // Counts every change to any project, so a version never goes back when a project empties and fills
    // again, and nothing is kept per project once nobody has it open.
    private long _version;

    // projectId -> connectionId -> user
    private readonly Dictionary<Guid, Dictionary<string, PresentUser>> _projects = [];

    public PresenceChange Join(Guid projectId, string connectionId, PresentUser user)
    {
        lock (_lock)
        {
            if (!_projects.TryGetValue(projectId, out var connections))
            {
                connections = [];
                _projects[projectId] = connections;
            }

            if (connections.ContainsKey(connectionId))
                return new PresenceChange(StateLocked(projectId), false);

            var alreadyPresent = connections.Values.Any(x => x.Id == user.Id);
            connections[connectionId] = user;

            return Result(projectId, changed: !alreadyPresent);
        }
    }

    public PresenceChange Leave(Guid projectId, string connectionId)
    {
        lock (_lock)
        {
            return LeaveLocked(projectId, connectionId);
        }
    }

    public IReadOnlyList<PresenceChange> LeaveAll(string connectionId)
    {
        lock (_lock)
        {
            return [.. _projects
                .Where(x => x.Value.ContainsKey(connectionId))
                .Select(x => x.Key)
                .ToList()
                .Select(projectId => LeaveLocked(projectId, connectionId))];
        }
    }

    public IReadOnlyList<Guid> ProjectsOf(string connectionId)
    {
        lock (_lock)
        {
            return [.. _projects.Where(x => x.Value.ContainsKey(connectionId)).Select(x => x.Key)];
        }
    }

    private PresenceChange LeaveLocked(Guid projectId, string connectionId)
    {
        if (!_projects.TryGetValue(projectId, out var connections) ||
            !connections.Remove(connectionId, out var user))
            return null;

        var stillPresent = connections.Values.Any(x => x.Id == user.Id);
        var result = Result(projectId, changed: !stillPresent);

        if (connections.Count == 0)
            _projects.Remove(projectId);

        return result;
    }

    // A change that leaves the users as they were keeps the version, so only real changes are sent on.
    private PresenceChange Result(Guid projectId, bool changed)
    {
        if (changed)
            _version++;

        return new PresenceChange(StateLocked(projectId), changed);
    }

    private PresenceState StateLocked(Guid projectId)
    {
        var users = _projects.TryGetValue(projectId, out var connections)
            ? connections.Values
                .GroupBy(x => x.Id)
                .Select(x => x.First())
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Id)
                .ToList()
            : [];

        return new PresenceState(projectId, _version, users);
    }
}
