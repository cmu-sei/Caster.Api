// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Caster.Api.Hubs;

namespace Caster.Api.Tests.Hubs;

/// <summary>
/// <see cref="ProjectPresenceTracker"/>: a user is present in a project while any of their connections has
/// it open, arriving with the first and departing with the last.
/// </summary>
public class ProjectPresenceTrackerTests
{
    private readonly ProjectPresenceTracker _tracker = new();
    private readonly Guid _project = Guid.NewGuid();
    private readonly PresentUser _alice = new(Guid.NewGuid(), "Alice");
    private readonly PresentUser _bob = new(Guid.NewGuid(), "bob");

    [Fact]
    public void Join_is_an_arrival_for_the_first_connection_only()
    {
        Assert.True(_tracker.Join(_project, "a1", _alice));
        Assert.False(_tracker.Join(_project, "a2", _alice));
        Assert.False(_tracker.Join(_project, "a1", _alice));
        Assert.True(_tracker.Join(_project, "b1", _bob));
    }

    [Fact]
    public void Join_to_another_project_is_an_arrival_there()
    {
        _tracker.Join(_project, "a1", _alice);

        Assert.True(_tracker.Join(Guid.NewGuid(), "a1", _alice));
    }

    [Fact]
    public void GetUsers_lists_each_user_once_ordered_by_name_ignoring_case()
    {
        _tracker.Join(_project, "b1", _bob);
        _tracker.Join(_project, "a1", _alice);
        _tracker.Join(_project, "a2", _alice);

        Assert.Equal([_alice, _bob], _tracker.GetUsers(_project));
        Assert.Empty(_tracker.GetUsers(Guid.NewGuid()));
    }

    [Fact]
    public void Leave_is_a_departure_for_the_last_connection_only()
    {
        _tracker.Join(_project, "a1", _alice);
        _tracker.Join(_project, "a2", _alice);

        Assert.Null(_tracker.Leave(_project, "a1"));
        Assert.Equal([_alice], _tracker.GetUsers(_project));
        Assert.Equal(_alice, _tracker.Leave(_project, "a2"));
        Assert.Empty(_tracker.GetUsers(_project));
    }

    [Fact]
    public void Leave_of_a_connection_that_never_joined_is_no_departure()
    {
        _tracker.Join(_project, "a1", _alice);

        Assert.Null(_tracker.Leave(_project, "b1"));
        Assert.Null(_tracker.Leave(Guid.NewGuid(), "a1"));
        Assert.Equal([_alice], _tracker.GetUsers(_project));
    }

    [Fact]
    public void LeaveAll_departs_only_the_projects_the_connection_was_the_last_one_to()
    {
        var other = Guid.NewGuid();
        _tracker.Join(_project, "a1", _alice);
        _tracker.Join(other, "a1", _alice);
        _tracker.Join(other, "a2", _alice);

        var departures = _tracker.LeaveAll("a1");

        Assert.Equal([new PresenceDeparture(_project, _alice)], departures);
        Assert.Equal([_alice], _tracker.GetUsers(other));
        Assert.Empty(_tracker.LeaveAll("a1"));
    }

    [Fact]
    public async Task Concurrent_tabs_of_one_user_arrive_once_and_depart_once()
    {
        var connections = Enumerable.Range(0, 64).Select(i => $"a{i}").ToArray();

        var arrivals = await Task.WhenAll(connections.Select(c => Task.Run(() => _tracker.Join(_project, c, _alice))));
        var departures = await Task.WhenAll(connections.Select(c => Task.Run(() => _tracker.Leave(_project, c))));

        Assert.Single(arrivals, x => x);
        Assert.Single(departures, x => x != null);
        Assert.Empty(_tracker.GetUsers(_project));
    }
}
