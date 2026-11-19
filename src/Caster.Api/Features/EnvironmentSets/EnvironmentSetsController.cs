// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Caster.Api.Features.EnvironmentSets;

[Route("api")]
[ApiController]
[Authorize]
public class EnvironmentSetsController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Get the API environment variable names (never values) that can be used as secrets, the
    /// secret source names in use that are missing, and how runs execute
    /// </summary>
    [HttpGet("environment-sets/secrets")]
    [ProducesResponseType(typeof(EnvironmentSecrets), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetEnvironmentSecrets")]
    public async Task<IActionResult> GetSecrets()
    {
        return Ok(await mediator.Send(new GetSecrets.Query()));
    }

    /// <summary>
    /// Get all Environment Sets
    /// </summary>
    [HttpGet("environment-sets")]
    [ProducesResponseType(typeof(IEnumerable<EnvironmentSet>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetEnvironmentSets")]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await mediator.Send(new GetAll.Query()));
    }

    /// <summary>
    /// Get an Environment Set by Id
    /// </summary>
    [HttpGet("environment-sets/{id:guid}")]
    [ProducesResponseType(typeof(EnvironmentSet), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetEnvironmentSet")]
    public async Task<IActionResult> Get([FromRoute] Guid id)
    {
        return Ok(await mediator.Send(new Get.Query { Id = id }));
    }

    /// <summary>
    /// Create an Environment Set
    /// </summary>
    [HttpPost("environment-sets")]
    [ProducesResponseType(typeof(EnvironmentSet), (int)HttpStatusCode.Created)]
    [SwaggerOperation(OperationId = "CreateEnvironmentSet")]
    public async Task<IActionResult> Create([FromBody] Create.Command command)
    {
        var result = await mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    /// <summary>
    /// Edit an Environment Set's name and description
    /// </summary>
    [HttpPut("environment-sets/{id:guid}")]
    [ProducesResponseType(typeof(EnvironmentSet), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "EditEnvironmentSet")]
    public async Task<IActionResult> Edit([FromRoute] Guid id, [FromBody] Edit.Command command)
    {
        command.Id = id;
        return Ok(await mediator.Send(command));
    }

    /// <summary>
    /// Delete an Environment Set, removing it from every Project it is assigned to
    /// </summary>
    [HttpDelete("environment-sets/{id:guid}")]
    [ProducesResponseType((int)HttpStatusCode.NoContent)]
    [SwaggerOperation(OperationId = "DeleteEnvironmentSet")]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        await mediator.Send(new Delete.Command { Id = id });
        return NoContent();
    }

    /// <summary>
    /// Add an entry to an Environment Set
    /// </summary>
    [HttpPost("environment-sets/{id:guid}/entries")]
    [ProducesResponseType(typeof(EnvironmentSet), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "CreateEnvironmentSetEntry")]
    public async Task<IActionResult> CreateEntry([FromRoute] Guid id, [FromBody] CreateEntry.Command command)
    {
        command.EnvironmentSetId = id;
        return Ok(await mediator.Send(command));
    }

    /// <summary>
    /// Edit an entry of an Environment Set
    /// </summary>
    [HttpPut("environment-sets/{id:guid}/entries/{entryId:guid}")]
    [ProducesResponseType(typeof(EnvironmentSet), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "EditEnvironmentSetEntry")]
    public async Task<IActionResult> EditEntry([FromRoute] Guid id, [FromRoute] Guid entryId, [FromBody] EditEntry.Command command)
    {
        command.EnvironmentSetId = id;
        command.Id = entryId;
        return Ok(await mediator.Send(command));
    }

    /// <summary>
    /// Delete an entry of an Environment Set
    /// </summary>
    [HttpDelete("environment-sets/{id:guid}/entries/{entryId:guid}")]
    [ProducesResponseType(typeof(EnvironmentSet), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "DeleteEnvironmentSetEntry")]
    public async Task<IActionResult> DeleteEntry([FromRoute] Guid id, [FromRoute] Guid entryId)
    {
        return Ok(await mediator.Send(new DeleteEntry.Command { EnvironmentSetId = id, Id = entryId }));
    }

    /// <summary>
    /// Copy an Environment Set with every entry. Secret entries copy the secret's name, never a
    /// value; the copy is assigned to no project
    /// </summary>
    [HttpPost("environment-sets/{id:guid}/copy")]
    [ProducesResponseType(typeof(EnvironmentSet), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "CopyEnvironmentSet")]
    public async Task<IActionResult> Copy([FromRoute] Guid id, [FromBody] Copy.Command command)
    {
        command ??= new Copy.Command();
        command.Id = id;
        return Ok(await mediator.Send(command));
    }

    /// <summary>
    /// Change entries of several Environment Sets at once, checked and saved together
    /// </summary>
    [HttpPost("environment-sets/batch-edit")]
    [ProducesResponseType(typeof(IEnumerable<EnvironmentSet>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "BatchEditEnvironmentSets")]
    public async Task<IActionResult> BatchEdit([FromBody] BatchEdit.Command command)
    {
        return Ok(await mediator.Send(command));
    }

    /// <summary>
    /// Get the infrastructure connections' fields and provider presets, for connection entries.
    /// Names and availability only, never values
    /// </summary>
    [HttpGet("environment-sets/connections")]
    [ProducesResponseType(typeof(IEnumerable<EnvironmentSetConnection>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetEnvironmentSetConnections")]
    public async Task<IActionResult> GetConnections()
    {
        return Ok(await mediator.Send(new GetConnections.Query()));
    }

    /// <summary>
    /// Create an Environment Set holding a provider preset's entries for an infrastructure connection
    /// </summary>
    [HttpPost("environment-sets/from-connection")]
    [ProducesResponseType(typeof(EnvironmentSet), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "CreateEnvironmentSetFromConnection")]
    public async Task<IActionResult> CreateFromConnection([FromBody] CreateFromConnection.Command command)
    {
        return Ok(await mediator.Send(command));
    }

    /// <summary>
    /// Get the Environment Sets assigned to a Project, without secret source names or values
    /// </summary>
    [HttpGet("projects/{projectId:guid}/environment-sets")]
    [ProducesResponseType(typeof(IEnumerable<ProjectEnvironmentSet>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetProjectEnvironmentSets")]
    public async Task<IActionResult> GetByProject([FromRoute] Guid projectId)
    {
        return Ok(await mediator.Send(new GetByProject.Query { ProjectId = projectId }));
    }

    /// <summary>
    /// Assign an Environment Set to a Project
    /// </summary>
    [HttpPost("projects/{projectId:guid}/environment-sets/{environmentSetId:guid}")]
    [ProducesResponseType(typeof(IEnumerable<ProjectEnvironmentSet>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "AssignEnvironmentSet")]
    public async Task<IActionResult> Assign([FromRoute] Guid projectId, [FromRoute] Guid environmentSetId)
    {
        return Ok(await mediator.Send(new Assign.Command { ProjectId = projectId, EnvironmentSetId = environmentSetId }));
    }

    /// <summary>
    /// Remove an Environment Set from a Project
    /// </summary>
    [HttpDelete("projects/{projectId:guid}/environment-sets/{environmentSetId:guid}")]
    [ProducesResponseType((int)HttpStatusCode.NoContent)]
    [SwaggerOperation(OperationId = "UnassignEnvironmentSet")]
    public async Task<IActionResult> Unassign([FromRoute] Guid projectId, [FromRoute] Guid environmentSetId)
    {
        await mediator.Send(new Unassign.Command { ProjectId = projectId, EnvironmentSetId = environmentSetId });
        return NoContent();
    }
}
