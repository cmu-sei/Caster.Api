// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Caster.Api.Hubs;

/// <summary>A user with a project open, as the other users in the project see them.</summary>
public record PresentUser(Guid Id, string Name);

/// <summary>A user whose last connection to a project has gone.</summary>
public record PresenceDeparture(Guid ProjectId, PresentUser User);

/// <summary>
/// Who has each project open, by SignalR connection, so that a user with the project open in several tabs
/// arrives with the first and departs with the last.
/// </summary>
/// <remarks>
/// In memory, like the hub groups it shadows: with more than one API instance each knows only the
/// connections made to it.
/// </remarks>
public interface IProjectPresenceTracker
{
    /// <summary>
    /// Records <paramref name="connectionId"/> as having <paramref name="projectId"/> open.
    /// </summary>
    /// <returns>
    /// True when this is the user's first connection to the project, the only time the others are told they
    /// arrived; false for a further tab, or a connection that had already joined.
    /// </returns>
    bool Join(Guid projectId, string connectionId, PresentUser user);

    /// <summary>Forgets that <paramref name="connectionId"/> has <paramref name="projectId"/> open.</summary>
    /// <returns>The user, when that was their last connection to the project; otherwise null.</returns>
    PresentUser Leave(Guid projectId, string connectionId);

    /// <summary>Forgets every project <paramref name="connectionId"/> had open, for a disconnect.</summary>
    /// <returns>Each project the connection was its user's last one to.</returns>
    IReadOnlyList<PresenceDeparture> LeaveAll(string connectionId);

    /// <summary>The users with <paramref name="projectId"/> open, each once, ordered by name.</summary>
    IReadOnlyList<PresentUser> GetUsers(Guid projectId);

    /// <summary>The projects <paramref name="connectionId"/> has open.</summary>
    IReadOnlyList<Guid> ProjectsOf(string connectionId);

    /// <summary>
    /// Holds <paramref name="projectId"/>'s presence until the result is disposed, so that a change and the
    /// notification of it are made together. Without it, the arrival of a user who has just left and come
    /// back can overtake the departure, and the others are left without them.
    /// </summary>
    Task<IDisposable> LockProjectAsync(Guid projectId, CancellationToken cancellationToken = default);
}

public class ProjectPresenceTracker : IProjectPresenceTracker
{
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _projectLocks = new();

    // projectId -> connectionId -> user
    private readonly Dictionary<Guid, Dictionary<string, PresentUser>> _projects = [];

    public bool Join(Guid projectId, string connectionId, PresentUser user)
    {
        lock (_lock)
        {
            if (!_projects.TryGetValue(projectId, out var connections))
            {
                connections = [];
                _projects[projectId] = connections;
            }

            if (connections.ContainsKey(connectionId))
                return false;

            var alreadyPresent = connections.Values.Any(x => x.Id == user.Id);
            connections[connectionId] = user;

            return !alreadyPresent;
        }
    }

    public PresentUser Leave(Guid projectId, string connectionId)
    {
        lock (_lock)
        {
            return LeaveLocked(projectId, connectionId);
        }
    }

    public IReadOnlyList<PresenceDeparture> LeaveAll(string connectionId)
    {
        lock (_lock)
        {
            var departures = new List<PresenceDeparture>();

            foreach (var projectId in _projects.Where(x => x.Value.ContainsKey(connectionId)).Select(x => x.Key).ToList())
            {
                var user = LeaveLocked(projectId, connectionId);

                if (user != null)
                    departures.Add(new PresenceDeparture(projectId, user));
            }

            return departures;
        }
    }

    public IReadOnlyList<PresentUser> GetUsers(Guid projectId)
    {
        lock (_lock)
        {
            if (!_projects.TryGetValue(projectId, out var connections))
                return [];

            return [.. connections.Values
                .GroupBy(x => x.Id)
                .Select(x => x.First())
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Id)];
        }
    }

    public IReadOnlyList<Guid> ProjectsOf(string connectionId)
    {
        lock (_lock)
        {
            return [.. _projects.Where(x => x.Value.ContainsKey(connectionId)).Select(x => x.Key)];
        }
    }

    public async Task<IDisposable> LockProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var gate = _projectLocks.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim _gate = gate;

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }

    private PresentUser LeaveLocked(Guid projectId, string connectionId)
    {
        if (!_projects.TryGetValue(projectId, out var connections) ||
            !connections.Remove(connectionId, out var user))
            return null;

        var stillPresent = connections.Values.Any(x => x.Id == user.Id);

        if (connections.Count == 0)
            _projects.Remove(projectId);

        return stillPresent ? null : user;
    }
}
