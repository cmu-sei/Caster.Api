// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Services.ProjectGroups;
using Caster.Api.Hubs;
using Crucible.Common.EntityEvents.Events;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using EntryEntity = Caster.Api.Domain.Models.EnvironmentSetEntry;
using ProjectSetEntity = Caster.Api.Domain.Models.ProjectEnvironmentSet;
using SetEntity = Caster.Api.Domain.Models.EnvironmentSet;

namespace Caster.Api.Features.EnvironmentSets.EventHandlers;

/// <summary>
/// Pushes Environment Set changes: the admin view to the EnvironmentSetsAdmin group
/// (ViewEnvironmentSets), and the member view of each affected project to that project's group
/// (ViewProject), so neither page has to reload.
/// </summary>
public class EnvironmentSetSignalRHandler(CasterContext db, IEnvironmentSetViews views, IHubContext<ProjectHub> projectHub) :
    INotificationHandler<EntityCreated<SetEntity>>,
    INotificationHandler<EntityUpdated<SetEntity>>,
    INotificationHandler<EntityDeleted<SetEntity>>,
    INotificationHandler<EntityCreated<EntryEntity>>,
    INotificationHandler<EntityUpdated<EntryEntity>>,
    INotificationHandler<EntityDeleted<EntryEntity>>,
    INotificationHandler<EntityCreated<ProjectSetEntity>>,
    INotificationHandler<EntityDeleted<ProjectSetEntity>>
{
    private static string AdminGroup => nameof(HubGroups.EnvironmentSetsAdmin);

    public async Task Handle(EntityCreated<SetEntity> notification, CancellationToken cancellationToken) =>
        await SendSet(notification.Entity.Id, EnvironmentSetHubMethods.EnvironmentSetCreated, cancellationToken);

    public async Task Handle(EntityUpdated<SetEntity> notification, CancellationToken cancellationToken)
    {
        await SendSet(notification.Entity.Id, EnvironmentSetHubMethods.EnvironmentSetUpdated, cancellationToken);
        await SendProjects(await AssignedProjects(notification.Entity.Id, cancellationToken), cancellationToken);
    }

    public async Task Handle(EntityDeleted<SetEntity> notification, CancellationToken cancellationToken) =>
        await projectHub.Clients.Group(AdminGroup)
            .SendAsync(EnvironmentSetHubMethods.EnvironmentSetDeleted, notification.Entity.Id, cancellationToken);

    public async Task Handle(EntityCreated<EntryEntity> notification, CancellationToken cancellationToken) =>
        await EntryChanged(notification.Entity.EnvironmentSetId, cancellationToken);

    public async Task Handle(EntityUpdated<EntryEntity> notification, CancellationToken cancellationToken) =>
        await EntryChanged(notification.Entity.EnvironmentSetId, cancellationToken);

    public async Task Handle(EntityDeleted<EntryEntity> notification, CancellationToken cancellationToken) =>
        await EntryChanged(notification.Entity.EnvironmentSetId, cancellationToken);

    public async Task Handle(EntityCreated<ProjectSetEntity> notification, CancellationToken cancellationToken) =>
        await AssignmentChanged(notification.Entity, cancellationToken);

    public async Task Handle(EntityDeleted<ProjectSetEntity> notification, CancellationToken cancellationToken) =>
        await AssignmentChanged(notification.Entity, cancellationToken);

    private async Task EntryChanged(Guid setId, CancellationToken cancellationToken)
    {
        // A set being deleted takes its entries with it; its deletion and its assignments' say all there is.
        if (await SendSet(setId, EnvironmentSetHubMethods.EnvironmentSetUpdated, cancellationToken))
        {
            await SendProjects(await AssignedProjects(setId, cancellationToken), cancellationToken);
        }
    }

    private async Task AssignmentChanged(ProjectSetEntity assignment, CancellationToken cancellationToken)
    {
        await SendSet(assignment.EnvironmentSetId, EnvironmentSetHubMethods.EnvironmentSetUpdated, cancellationToken);
        await SendProjects([assignment.ProjectId], cancellationToken);
    }

    /// <returns>False if the set no longer exists.</returns>
    private async Task<bool> SendSet(Guid setId, string method, CancellationToken cancellationToken)
    {
        var set = await views.GetSet(setId, cancellationToken);

        if (set is null)
        {
            return false;
        }

        await projectHub.Clients.Group(AdminGroup).SendAsync(method, set, cancellationToken);
        return true;
    }

    private async Task SendProjects(IEnumerable<Guid> projectIds, CancellationToken cancellationToken)
    {
        foreach (var projectId in projectIds.Distinct())
        {
            var sets = await views.GetProjectSets(projectId, cancellationToken);

            await projectHub.Clients.Group(projectId.ToString())
                .SendAsync(EnvironmentSetHubMethods.ProjectEnvironmentSetsUpdated, projectId, sets, cancellationToken);
        }
    }

    /// <summary>Every project the set reaches, directly or through a group.</summary>
    private async Task<List<Guid>> AssignedProjects(Guid setId, CancellationToken cancellationToken) =>
        await db.EffectiveSetAssignments()
            .Where(x => x.EnvironmentSetId == setId)
            .Select(x => x.ProjectId)
            .Distinct()
            .ToListAsync(cancellationToken);
}

public static class EnvironmentSetHubMethods
{
    public const string EnvironmentSetCreated = nameof(EnvironmentSetCreated);
    public const string EnvironmentSetUpdated = nameof(EnvironmentSetUpdated);
    public const string EnvironmentSetDeleted = nameof(EnvironmentSetDeleted);

    /// <summary>Sent to a project's group with the project id and its sets (member view).</summary>
    public const string ProjectEnvironmentSetsUpdated = nameof(ProjectEnvironmentSetsUpdated);
}
