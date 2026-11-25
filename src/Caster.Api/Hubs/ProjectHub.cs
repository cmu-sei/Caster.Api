// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Caster.Api.Hubs;

[Authorize]
public class ProjectHub : Hub
{
    private readonly IOutputService _outputService;
    private readonly CasterContext _db;
    private readonly ICasterAuthorizationService _authorizationService;
    private readonly IProjectPresenceTracker _presence;
    private readonly ILogger<ProjectHub> _logger;

    public ProjectHub(
        IOutputService outputService,
        CasterContext db,
        ICasterAuthorizationService authorizationService,
        IProjectPresenceTracker presence,
        ILogger<ProjectHub> logger)
    {
        _outputService = outputService;
        _db = db;
        _authorizationService = authorizationService;
        _presence = presence;
        _logger = logger;
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        // Forget the connection first, so that a send that fails can't leave it listed.
        foreach (var change in _presence.LeaveAll(Context.ConnectionId).Where(x => x.Changed))
        {
            await TrySendPresence(change.State);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Adds the connection to the project's group and to the project's presence, telling the group who is
    /// there when this is the caller's first connection to it.
    /// </summary>
    /// <returns>
    /// Everyone with the project open, the caller included, as of a version. An update can reach the caller
    /// before this answer does, so the caller keeps whichever has the higher version.
    /// </returns>
    public async Task<PresenceState> JoinProject(Guid projectId)
    {
        if (!await _authorizationService.Authorize<Project>(projectId, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], Context.ConnectionAborted))
            throw new ForbiddenException();

        await Groups.AddToGroupAsync(Context.ConnectionId, projectId.ToString());

        var user = await GetPresentUser();
        var change = _presence.Join(projectId, Context.ConnectionId, user);

        // The connection can have closed while this was waiting. Its disconnect has then already looked for
        // what it had open and may have found nothing, so a join that registered it now must not leave it
        // registered.
        if (Context.ConnectionAborted.IsCancellationRequested)
        {
            var left = _presence.Leave(projectId, Context.ConnectionId);

            if (left is { Changed: true })
                await TrySendPresence(left.State);

            return left?.State ?? change.State;
        }

        if (change.Changed)
            await TrySendPresence(change.State);

        return change.State;
    }

    public async Task LeaveProject(Guid projectId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, projectId.ToString());

        var change = _presence.Leave(projectId, Context.ConnectionId);

        if (change is { Changed: true })
            await TrySendPresence(change.State);
    }

    public async Task JoinProjectAdmin(Guid projectId)
    {
        if (!await _authorizationService.Authorize<Project>(projectId, [SystemPermission.ViewProjects], [ProjectPermission.ManageProject], Context.ConnectionAborted))
            throw new ForbiddenException();

        await Groups.AddToGroupAsync(Context.ConnectionId, ProjectHubMethods.GetProjectAdminGroup(projectId));
    }

    public async Task LeaveProjectAdmin(Guid projectId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, ProjectHubMethods.GetProjectAdminGroup(projectId));
    }

    public async Task JoinGroup(Guid groupId)
    {
        if (!await _authorizationService.Authorize<Group>(groupId, [SystemPermission.ViewGroups], [GroupPermission.ManageMembership], Context.ConnectionAborted))
            throw new ForbiddenException();

        await Groups.AddToGroupAsync(Context.ConnectionId, groupId.ToString());
    }

    public async Task LeaveGroup(Guid groupId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId.ToString());
    }

    public async Task JoinWorkspace(Guid workspaceId)
    {
        if (!await _authorizationService.Authorize<Workspace>(workspaceId, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], Context.ConnectionAborted))
            throw new ForbiddenException();

        await Groups.AddToGroupAsync(Context.ConnectionId, workspaceId.ToString());
    }

    public async Task LeaveWorkspace(Guid workspaceId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, workspaceId.ToString());
    }

    public async Task JoinDesign(Guid designId)
    {
        if (!await _authorizationService.Authorize<Design>(designId, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], Context.ConnectionAborted))
            throw new ForbiddenException();

        await Groups.AddToGroupAsync(Context.ConnectionId, designId.ToString());
    }

    public async Task LeaveDesign(Guid designId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, designId.ToString());
    }

    public async Task JoinWorkspacesAdmin()
    {
        if (!await _authorizationService.Authorize([SystemPermission.ViewWorkspaces], Context.ConnectionAborted))
            throw new ForbiddenException();

        await Groups.AddToGroupAsync(Context.ConnectionId, nameof(HubGroups.WorkspacesAdmin));
    }

    public async Task LeaveWorkspacesAdmin()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, nameof(HubGroups.WorkspacesAdmin));
    }

    public async Task JoinVlansAdmin()
    {
        if (!await _authorizationService.Authorize([SystemPermission.ViewVLANs], Context.ConnectionAborted))
            throw new ForbiddenException();

        await Groups.AddToGroupAsync(Context.ConnectionId, nameof(HubGroups.VlansAdmin));
    }

    public async Task LeaveVlansAdmin()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, nameof(HubGroups.VlansAdmin));
    }

    public async Task JoinRolesAdmin()
    {
        if (!await _authorizationService.Authorize([SystemPermission.ViewRoles], Context.ConnectionAborted))
            throw new ForbiddenException();

        await Groups.AddToGroupAsync(Context.ConnectionId, nameof(HubGroups.RolesAdmin));
    }

    public async Task LeaveRolesAdmin()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, nameof(HubGroups.RolesAdmin));
    }

    public async Task JoinEnvironmentSetsAdmin()
    {
        if (!await _authorizationService.Authorize([SystemPermission.ViewEnvironmentSets], Context.ConnectionAborted))
            throw new ForbiddenException();

        await Groups.AddToGroupAsync(Context.ConnectionId, nameof(HubGroups.EnvironmentSetsAdmin));
    }

    public async Task LeaveEnvironmentSetsAdmin()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, nameof(HubGroups.EnvironmentSetsAdmin));
    }

    private Task SendPresence(PresenceState state) =>
        Clients.Group(state.ProjectId.ToString()).SendAsync(ProjectHubMethods.PresenceChanged, state);

    // A send that fails only costs the others one update. The tracker has already changed by then, so it
    // must not fail the caller's join or leave, or stop the sends after it.
    private async Task TrySendPresence(PresenceState state)
    {
        try
        {
            await SendPresence(state);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send presence for project {ProjectId}", state.ProjectId);
        }
    }

    /// <summary>
    /// The caller as the others see them: the token's name, else the name Caster stored for the user, else
    /// their id.
    /// </summary>
    private async Task<PresentUser> GetPresentUser()
    {
        var id = Context.User.GetId();
        var name = Context.User.FindFirst("name")?.Value;

        if (string.IsNullOrWhiteSpace(name))
        {
            name = await _db.Users
                .Where(x => x.Id == id)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(Context.ConnectionAborted);
        }

        return new PresentUser(id, string.IsNullOrWhiteSpace(name) ? id.ToString() : name);
    }

    #region RunOutput

    enum OutputType
    {
        Plan,
        Apply
    }

    private async IAsyncEnumerable<string> GetOutput(Guid id, OutputType type, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var output = _outputService.GetOutput(id);

        if (output == null)
        {
            string dbOutput = await this.GetDbOutput(id, type, cancellationToken);

            if (dbOutput != null)
            {
                yield return dbOutput;
            }

            yield break;
        }

        var resetEvent = output.Subscribe();
        var sent = string.Empty;
        bool done = false;

        do
        {
            if (output.Complete)
            {
                done = true;
            }

            var newContent = output.Content.Substring(sent.Length);

            yield return newContent;
            sent += newContent;

            if (!done)
            {
                try
                {
                    await resetEvent.WaitAsync(cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    done = true;
                }
            }
        }
        while (!done);

        output.Unsubscribe(resetEvent);
    }

    private async Task<string> GetDbOutput(Guid id, OutputType type, CancellationToken cancellationToken)
    {
        string dbOutput = string.Empty;

        if (type == OutputType.Plan)
        {
            dbOutput = await _db.Plans
                .Where(p => p.Id == id)
                .Select(p => p.Output)
                .FirstOrDefaultAsync(cancellationToken);
        }
        else if (type == OutputType.Apply)
        {
            dbOutput = await _db.Applies
                .Where(p => p.Id == id)
                .Select(p => p.Output)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return dbOutput;
    }

    public async IAsyncEnumerable<string> GetPlanOutput(Guid id, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!await _authorizationService.Authorize<Plan>(id, [SystemPermission.ViewProjects, SystemPermission.ViewWorkspaces], [ProjectPermission.ViewProject], Context.ConnectionAborted))
            throw new ForbiddenException();

        await foreach (var output in this.GetOutput(id, OutputType.Plan, cancellationToken))
        {
            yield return output;
        }
    }

    public async IAsyncEnumerable<string> GetApplyOutput(Guid id, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!await _authorizationService.Authorize<Apply>(id, [SystemPermission.ViewProjects, SystemPermission.ViewWorkspaces], [ProjectPermission.ViewProject], Context.ConnectionAborted))
            throw new ForbiddenException();

        await foreach (var output in this.GetOutput(id, OutputType.Apply, cancellationToken))
        {
            yield return output;
        }
    }

    #endregion
}

public static class ProjectHubMethods
{
    public const string DesignCreated = "DesignCreated";
    public const string DesignUpdated = "DesignUpdated";
    public const string DesignDeleted = "DesignDeleted";

    public const string VariableCreated = "VariableCreated";
    public const string VariableUpdated = "VariableUpdated";
    public const string VariableDeleted = "VariableDeleted";

    public const string GroupMembershipCreated = nameof(GroupMembershipCreated);
    public const string GroupMembershipUpdated = nameof(GroupMembershipUpdated);
    public const string GroupMembershipDeleted = nameof(GroupMembershipDeleted);

    public const string ProjectMembershipCreated = nameof(ProjectMembershipCreated);
    public const string ProjectMembershipUpdated = nameof(ProjectMembershipUpdated);
    public const string ProjectMembershipDeleted = nameof(ProjectMembershipDeleted);

    public const string PresenceChanged = nameof(PresenceChanged);

    private const string GroupSeparator = "-";

    public static string GetProjectAdminGroup(Guid projectId)
    {
        return $"{HubGroups.ProjectsAdmin}{GroupSeparator}{projectId}";
    }
}
