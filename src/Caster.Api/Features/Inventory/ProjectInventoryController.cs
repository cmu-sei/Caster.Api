// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Threading.Tasks;
using Caster.Api.Domain.Services.Inventory;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Caster.Api.Features.Inventory;

/// <summary>
/// A project's inventory: the connections assigned to it, read with ViewProject. The routes never
/// fall back to a connection the project hasn't been given.
/// </summary>
[Route("api/projects/{projectId:guid}/inventory")]
[ApiController]
[Authorize]
public class ProjectInventoryController(IMediator mediator) : ControllerBase
{
    /// <summary>Get the inventory connections assigned to a project, with their status.</summary>
    [HttpGet("connections")]
    [ProducesResponseType(typeof(InventoryConnection[]), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetProjectInventoryConnections")]
    public async Task<IActionResult> GetConnections([FromRoute] Guid projectId) =>
        Ok(await mediator.Send(new GetProjectConnections.Query { ProjectId = projectId }));

    /// <summary>Assign an inventory connection to a project, so its members see the connection's inventory.</summary>
    [HttpPost("connections/{connectionName}")]
    [ProducesResponseType(typeof(InventoryConnection[]), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "AssignProjectInventoryConnection")]
    public async Task<IActionResult> Assign([FromRoute] Guid projectId, [FromRoute] string connectionName) =>
        Ok(await mediator.Send(new AssignConnection.Command { ProjectId = projectId, ConnectionName = connectionName }));

    /// <summary>Unassign an inventory connection from a project.</summary>
    [HttpDelete("connections/{connectionName}")]
    [ProducesResponseType(typeof(InventoryConnection[]), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "UnassignProjectInventoryConnection")]
    public async Task<IActionResult> Unassign([FromRoute] Guid projectId, [FromRoute] string connectionName) =>
        Ok(await mediator.Send(new UnassignConnection.Command { ProjectId = projectId, ConnectionName = connectionName }));

    /// <summary>Get the VM templates on a project's connection, its default unless connection names another.</summary>
    [HttpGet("vm-templates")]
    [ProducesResponseType(typeof(InventoryResult), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetProjectInventoryVmTemplates")]
    public Task<IActionResult> GetVmTemplates([FromRoute] Guid projectId, [FromQuery] string connection = null) =>
        Get(projectId, InventoryCategory.VmTemplate, connection);

    /// <summary>Get the ISOs on a project's connection.</summary>
    [HttpGet("isos")]
    [ProducesResponseType(typeof(InventoryResult), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetProjectInventoryIsos")]
    public Task<IActionResult> GetIsos([FromRoute] Guid projectId, [FromQuery] string connection = null) =>
        Get(projectId, InventoryCategory.Iso, connection);

    /// <summary>Get the networks on a project's connection.</summary>
    [HttpGet("networks")]
    [ProducesResponseType(typeof(InventoryResult), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetProjectInventoryNetworks")]
    public Task<IActionResult> GetNetworks([FromRoute] Guid projectId, [FromQuery] string connection = null) =>
        Get(projectId, InventoryCategory.Network, connection);

    /// <summary>Get the datastores on a project's connection.</summary>
    [HttpGet("datastores")]
    [ProducesResponseType(typeof(InventoryResult), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetProjectInventoryDatastores")]
    public Task<IActionResult> GetDatastores([FromRoute] Guid projectId, [FromQuery] string connection = null) =>
        Get(projectId, InventoryCategory.Datastore, connection);

    /// <summary>Get the content library templates on a project's connection.</summary>
    [HttpGet("content-library")]
    [ProducesResponseType(typeof(InventoryResult), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetProjectInventoryContentLibrary")]
    public Task<IActionResult> GetContentLibrary([FromRoute] Guid projectId, [FromQuery] string connection = null) =>
        Get(projectId, InventoryCategory.ContentLibrary, connection);

    /// <summary>Get the hosts of a project's connection.</summary>
    [HttpGet("hosts")]
    [ProducesResponseType(typeof(InventoryResult), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetProjectInventoryHosts")]
    public Task<IActionResult> GetHosts([FromRoute] Guid projectId, [FromQuery] string connection = null) =>
        Get(projectId, InventoryCategory.Host, connection);

    /// <summary>Refresh a project's connection, waiting a bounded time for it to land.</summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(InventoryRefreshResult), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "RefreshProjectInventory")]
    public async Task<IActionResult> Refresh([FromRoute] Guid projectId, [FromQuery] string connection = null) =>
        Ok(await mediator.Send(new RefreshProjectInventory.Command { ProjectId = projectId, Connection = connection }));

    private async Task<IActionResult> Get(Guid projectId, InventoryCategory category, string connection) =>
        Ok(await mediator.Send(new GetProjectInventory.Query { ProjectId = projectId, Category = category, Connection = connection }));
}
