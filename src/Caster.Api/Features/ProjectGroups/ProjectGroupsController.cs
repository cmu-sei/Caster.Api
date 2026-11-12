// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Caster.Api.Features.ProjectGroups;

/// <summary>
/// Project groups: projects that share memberships, Environment Sets, inventory connections, a
/// partition and hosts.
/// </summary>
[Route("api/project-groups")]
[ApiController]
[Authorize]
public class ProjectGroupsController(IMediator mediator) : ControllerBase
{
    /// <summary>Get the project groups the caller can see</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ProjectGroup[]), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetProjectGroups")]
    public async Task<IActionResult> GetAll() => Ok(await mediator.Send(new GetAll.Query()));

    /// <summary>Get a project group</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProjectGroup), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetProjectGroup")]
    public async Task<IActionResult> Get([FromRoute] Guid id) => Ok(await mediator.Send(new Get.Query { Id = id }));

    /// <summary>Create a project group</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ProjectGroup), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "CreateProjectGroup")]
    public async Task<IActionResult> Create([FromBody] Create.Command command) => Ok(await mediator.Send(command));

    /// <summary>Edit a project group's name and description</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ProjectGroup), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "EditProjectGroup")]
    public async Task<IActionResult> Edit([FromRoute] Guid id, [FromBody] Edit.Command command)
    {
        command.Id = id;
        return Ok(await mediator.Send(command));
    }

    /// <summary>Delete a project group. Its projects and hosts stay, ungrouped</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType((int)HttpStatusCode.NoContent)]
    [SwaggerOperation(OperationId = "DeleteProjectGroup")]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        await mediator.Send(new Delete.Command { Id = id });
        return NoContent();
    }

    /// <summary>Get a project group's memberships</summary>
    [HttpGet("{id:guid}/memberships")]
    [ProducesResponseType(typeof(ProjectGroupMembership[]), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetProjectGroupMemberships")]
    public async Task<IActionResult> GetMemberships([FromRoute] Guid id) =>
        Ok(await mediator.Send(new GetMemberships.Query { ProjectGroupId = id }));

    /// <summary>Give a user or group a project role in every project of a project group</summary>
    [HttpPost("{id:guid}/memberships")]
    [ProducesResponseType(typeof(ProjectGroupMembership[]), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "CreateProjectGroupMembership")]
    public async Task<IActionResult> CreateMembership([FromRoute] Guid id, [FromBody] CreateMembership.Command command)
    {
        command.ProjectGroupId = id;
        return Ok(await mediator.Send(command));
    }

    /// <summary>Change a project group membership's role</summary>
    [HttpPut("memberships/{membershipId:guid}")]
    [ProducesResponseType(typeof(ProjectGroupMembership[]), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "EditProjectGroupMembership")]
    public async Task<IActionResult> EditMembership([FromRoute] Guid membershipId, [FromBody] EditMembership.Command command)
    {
        command.Id = membershipId;
        return Ok(await mediator.Send(command));
    }

    /// <summary>Remove a project group membership</summary>
    [HttpDelete("memberships/{membershipId:guid}")]
    [ProducesResponseType(typeof(ProjectGroupMembership[]), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "DeleteProjectGroupMembership")]
    public async Task<IActionResult> DeleteMembership([FromRoute] Guid membershipId) =>
        Ok(await mediator.Send(new DeleteMembership.Command { Id = membershipId }));

    /// <summary>Put a project in a project group, moving it out of any other</summary>
    [HttpPost("{id:guid}/projects/{projectId:guid}")]
    [ProducesResponseType(typeof(ProjectGroup), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "AddProjectToProjectGroup")]
    public async Task<IActionResult> AddProject([FromRoute] Guid id, [FromRoute] Guid projectId) =>
        Ok(await mediator.Send(new AddProject.Command { ProjectGroupId = id, ProjectId = projectId }));

    /// <summary>Take a project out of a project group</summary>
    [HttpDelete("{id:guid}/projects/{projectId:guid}")]
    [ProducesResponseType(typeof(ProjectGroup), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "RemoveProjectFromProjectGroup")]
    public async Task<IActionResult> RemoveProject([FromRoute] Guid id, [FromRoute] Guid projectId) =>
        Ok(await mediator.Send(new RemoveProject.Command { ProjectGroupId = id, ProjectId = projectId }));

    /// <summary>Give an Environment Set to every project in a project group</summary>
    [HttpPost("{id:guid}/environment-sets/{environmentSetId:guid}")]
    [ProducesResponseType(typeof(ProjectGroup), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "AssignEnvironmentSetToProjectGroup")]
    public async Task<IActionResult> AssignSet([FromRoute] Guid id, [FromRoute] Guid environmentSetId) =>
        Ok(await mediator.Send(new AssignSet.Command { ProjectGroupId = id, EnvironmentSetId = environmentSetId }));

    /// <summary>Take an Environment Set from a project group</summary>
    [HttpDelete("{id:guid}/environment-sets/{environmentSetId:guid}")]
    [ProducesResponseType(typeof(ProjectGroup), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "UnassignEnvironmentSetFromProjectGroup")]
    public async Task<IActionResult> UnassignSet([FromRoute] Guid id, [FromRoute] Guid environmentSetId) =>
        Ok(await mediator.Send(new UnassignSet.Command { ProjectGroupId = id, EnvironmentSetId = environmentSetId }));

    /// <summary>Show an inventory connection to every project in a project group</summary>
    [HttpPost("{id:guid}/inventory-connections/{connectionName}")]
    [ProducesResponseType(typeof(ProjectGroup), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "AssignInventoryConnectionToProjectGroup")]
    public async Task<IActionResult> AssignConnection([FromRoute] Guid id, [FromRoute] string connectionName) =>
        Ok(await mediator.Send(new AssignConnection.Command { ProjectGroupId = id, ConnectionName = connectionName }));

    /// <summary>Stop showing an inventory connection to a project group</summary>
    [HttpDelete("{id:guid}/inventory-connections/{connectionName}")]
    [ProducesResponseType(typeof(ProjectGroup), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "UnassignInventoryConnectionFromProjectGroup")]
    public async Task<IActionResult> UnassignConnection([FromRoute] Guid id, [FromRoute] string connectionName) =>
        Ok(await mediator.Send(new UnassignConnection.Command { ProjectGroupId = id, ConnectionName = connectionName }));

    /// <summary>Set the partition a project group's projects acquire VLANs from when they have none</summary>
    [HttpPut("{id:guid}/partition")]
    [ProducesResponseType(typeof(ProjectGroup), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "SetProjectGroupPartition")]
    public async Task<IActionResult> SetPartition([FromRoute] Guid id, [FromBody] SetPartition.Command command)
    {
        command.ProjectGroupId = id;
        return Ok(await mediator.Send(command));
    }
}
