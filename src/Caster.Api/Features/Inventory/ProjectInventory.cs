// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.Inventory;

/// <summary>
/// The connections a project's members can see the inventory of, and which one a request reads.
/// Reading a project's inventory needs ViewProject (or ViewProjects) and the connection being
/// assigned to the project; ViewInfrastructure holders can read any connection.
/// </summary>
public class ProjectInventoryConnections(
    CasterContext dbContext,
    IInventoryService inventoryService,
    ICasterAuthorizationService authorizationService)
{
    public const string NoConnection = "No inventory connection is assigned to this project.";

    public async Task<bool> CanRead(Guid projectId, CancellationToken cancellationToken) =>
        CanSeeEverything() ||
        await authorizationService.Authorize<Project>(projectId, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);

    /// <summary>The project's assignments, each with its status, in configuration order, then missing ones by name.</summary>
    public async Task<InventoryConnection[]> GetAssigned(Guid projectId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Projects.AnyAsync(x => x.Id == projectId, cancellationToken))
            throw new EntityNotFoundException<Project>();

        var assigned = await dbContext.ProjectInventoryConnections
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Select(x => x.ConnectionName)
            .ToListAsync(cancellationToken);

        var configured = inventoryService.GetConnections();
        var names = assigned.Select(ProjectInventoryConnection.Normalize).ToHashSet();
        var usable = configured.Where(x => names.Contains(ProjectInventoryConnection.Normalize(x.Name))).ToList();
        var fallback = usable.FirstOrDefault(x => x.Enabled);

        var present = usable.Select(x =>
        {
            var connection = InventoryConnection.From(x);
            connection.IsDefault = x == fallback;
            return connection;
        });

        var missing = assigned
            .Where(x => !configured.Any(c => ProjectInventoryConnection.Normalize(c.Name) == ProjectInventoryConnection.Normalize(x)))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(x => new InventoryConnection { Name = x, Status = InventoryConnectionStatus.Missing });

        return present.Concat(missing).ToArray();
    }

    /// <summary>
    /// The connection a project request reads: the one named, which must be assigned unless the
    /// caller holds ViewInfrastructure, or else the project's default, the first assigned one in
    /// configuration order with inventory enabled. Null when the project has none to read.
    /// </summary>
    public async Task<string> Resolve(Guid projectId, string requested, CancellationToken cancellationToken)
    {
        var assigned = await GetAssigned(projectId, cancellationToken);

        if (string.IsNullOrWhiteSpace(requested))
            return assigned.FirstOrDefault(x => x.IsDefault)?.Name;

        var match = assigned.FirstOrDefault(x =>
            x.Status == InventoryConnectionStatus.Available &&
            ProjectInventoryConnection.Normalize(x.Name) == ProjectInventoryConnection.Normalize(requested));

        if (match is not null)
            return match.Name;

        if (CanSeeEverything() && inventoryService.HasConnection(requested))
            return requested.Trim();

        throw new EntityNotFoundException<InventoryConnection>($"Inventory connection '{requested}' is not assigned to this project.");
    }

    private bool CanSeeEverything() =>
        authorizationService.GetSystemPermissions().Contains(SystemPermission.ViewInfrastructure);
}

/// <summary>A project's assigned connections, with their status.</summary>
public class GetProjectConnections
{
    [DataContract(Name = "GetProjectInventoryConnectionsQuery")]
    public class Query : IRequest<InventoryConnection[]>
    {
        [JsonIgnore]
        public Guid ProjectId { get; set; }
    }

    public class Handler(ProjectInventoryConnections connections) : BaseHandler<Query, InventoryConnection[]>
    {
        public override Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            connections.CanRead(request.ProjectId, cancellationToken);

        public override Task<InventoryConnection[]> HandleRequest(Query request, CancellationToken cancellationToken) =>
            connections.GetAssigned(request.ProjectId, cancellationToken);
    }
}

/// <summary>One kind of object from a project's connection.</summary>
public class GetProjectInventory
{
    [DataContract(Name = "GetProjectInventoryQuery")]
    public class Query : IRequest<InventoryResult>
    {
        [JsonIgnore]
        public Guid ProjectId { get; set; }

        [JsonIgnore]
        public InventoryCategory Category { get; set; }

        /// <summary>The connection, by name. Null or empty reads the project's default.</summary>
        public string Connection { get; set; }
    }

    public class Handler(ProjectInventoryConnections connections, IInventoryService inventoryService) : BaseHandler<Query, InventoryResult>
    {
        public override Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            connections.CanRead(request.ProjectId, cancellationToken);

        public override async Task<InventoryResult> HandleRequest(Query request, CancellationToken cancellationToken)
        {
            var name = await connections.Resolve(request.ProjectId, request.Connection, cancellationToken);

            // Never the service's global default: a project with no connection gets nothing.
            if (name is null)
                return InventoryResult.NoConnection(ProjectInventoryConnections.NoConnection);

            return InventoryResult.From(InventoryConnection.Snapshot(inventoryService, name), request.Category, name);
        }
    }
}

/// <summary>Refreshes a project's connection. Anyone who can see a connection can refresh it.</summary>
public class RefreshProjectInventory
{
    [DataContract(Name = "RefreshProjectInventoryCommand")]
    public class Command : IRequest<InventoryRefreshResult>
    {
        [JsonIgnore]
        public Guid ProjectId { get; set; }

        public string Connection { get; set; }
    }

    public class Handler(ProjectInventoryConnections connections, IInventoryService inventoryService) : BaseHandler<Command, InventoryRefreshResult>
    {
        public override Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            connections.CanRead(request.ProjectId, cancellationToken);

        public override async Task<InventoryRefreshResult> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var name = await connections.Resolve(request.ProjectId, request.Connection, cancellationToken);

            if (name is null)
                return new InventoryRefreshResult { Completed = true, WaitSeconds = 0 };

            var completed = await inventoryService.ForceRefreshAsync(name, InventoryDefaults.ForceRefreshTimeout, cancellationToken);

            return new InventoryRefreshResult
            {
                Completed = completed,
                LastUpdated = inventoryService.GetSnapshot(name)?.LastUpdated,
                WaitSeconds = (int)InventoryDefaults.ForceRefreshTimeout.TotalSeconds
            };
        }
    }
}

/// <summary>
/// Shows a connection's inventory to a project's members. Needs ManageInfrastructure, not a
/// project permission, so a project's managers can't give themselves a hypervisor.
/// </summary>
public class AssignConnection
{
    [DataContract(Name = "AssignInventoryConnectionCommand")]
    public class Command : IRequest<InventoryConnection[]>
    {
        [JsonIgnore]
        public Guid ProjectId { get; set; }

        [JsonIgnore]
        public string ConnectionName { get; set; }
    }

    public class Handler(
        ICasterAuthorizationService authorizationService,
        CasterContext dbContext,
        IInfrastructureConnectionCatalog catalog,
        ProjectInventoryConnections connections) : BaseHandler<Command, InventoryConnection[]>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageInfrastructure], cancellationToken);

        public override async Task<InventoryConnection[]> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            if (!await dbContext.Projects.AnyAsync(x => x.Id == request.ProjectId, cancellationToken))
                throw new EntityNotFoundException<Project>();

            var connection = catalog.Find(request.ConnectionName)
                ?? throw new EntityNotFoundException<InventoryConnection>($"Inventory connection '{request.ConnectionName}' is not configured.");

            var normalized = ProjectInventoryConnection.Normalize(connection.Name);

            if (!await dbContext.ProjectInventoryConnections.AnyAsync(
                    x => x.ProjectId == request.ProjectId && x.NormalizedConnectionName == normalized, cancellationToken))
            {
                dbContext.ProjectInventoryConnections.Add(new ProjectInventoryConnection
                {
                    ProjectId = request.ProjectId,
                    ConnectionName = connection.Name,
                    NormalizedConnectionName = normalized
                });

                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return await connections.GetAssigned(request.ProjectId, cancellationToken);
        }
    }
}

public class UnassignConnection
{
    [DataContract(Name = "UnassignInventoryConnectionCommand")]
    public class Command : IRequest<InventoryConnection[]>
    {
        [JsonIgnore]
        public Guid ProjectId { get; set; }

        [JsonIgnore]
        public string ConnectionName { get; set; }
    }

    public class Handler(
        ICasterAuthorizationService authorizationService,
        CasterContext dbContext,
        ProjectInventoryConnections connections) : BaseHandler<Command, InventoryConnection[]>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageInfrastructure], cancellationToken);

        /// <summary>Takes a missing connection's name too, so a stale assignment can be removed.</summary>
        public override async Task<InventoryConnection[]> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var normalized = ProjectInventoryConnection.Normalize(request.ConnectionName);

            var assignment = await dbContext.ProjectInventoryConnections.SingleOrDefaultAsync(
                    x => x.ProjectId == request.ProjectId && x.NormalizedConnectionName == normalized, cancellationToken)
                ?? throw new EntityNotFoundException<InventoryConnection>($"Inventory connection '{request.ConnectionName}' is not assigned to this project.");

            dbContext.ProjectInventoryConnections.Remove(assignment);
            await dbContext.SaveChangesAsync(cancellationToken);

            return await connections.GetAssigned(request.ProjectId, cancellationToken);
        }
    }
}
