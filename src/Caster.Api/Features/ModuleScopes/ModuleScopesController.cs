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

namespace Caster.Api.Features.ModuleScopes;

[Route("api")]
[ApiController]
[Authorize]
public class ModuleScopesController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Get all Module Scopes
    /// </summary>
    [HttpGet("module-scopes")]
    [ProducesResponseType(typeof(IEnumerable<ModuleScope>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetModuleScopes")]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await mediator.Send(new GetAll.Query()));
    }

    /// <summary>
    /// Get a Module Scope by Id
    /// </summary>
    [HttpGet("module-scopes/{id:guid}")]
    [ProducesResponseType(typeof(ModuleScope), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetModuleScope")]
    public async Task<IActionResult> Get([FromRoute] Guid id)
    {
        return Ok(await mediator.Send(new Get.Query { Id = id }));
    }

    /// <summary>
    /// Create a Module Scope
    /// </summary>
    [HttpPost("module-scopes")]
    [ProducesResponseType(typeof(ModuleScope), (int)HttpStatusCode.Created)]
    [SwaggerOperation(OperationId = "CreateModuleScope")]
    public async Task<IActionResult> Create([FromBody] Create.Command command)
    {
        var result = await mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    /// <summary>
    /// Edit a Module Scope's name, description and paths
    /// </summary>
    [HttpPut("module-scopes/{id:guid}")]
    [ProducesResponseType(typeof(ModuleScope), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "EditModuleScope")]
    public async Task<IActionResult> Edit([FromRoute] Guid id, [FromBody] Edit.Command command)
    {
        command.Id = id;
        return Ok(await mediator.Send(command));
    }

    /// <summary>
    /// Delete a Module Scope, removing it from every Project and Project Group it is assigned to
    /// </summary>
    /// <remarks>
    /// A project left with no scope may use every Module again.
    /// </remarks>
    [HttpDelete("module-scopes/{id:guid}")]
    [ProducesResponseType((int)HttpStatusCode.NoContent)]
    [SwaggerOperation(OperationId = "DeleteModuleScope")]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        await mediator.Send(new Delete.Command { Id = id });
        return NoContent();
    }

    /// <summary>
    /// Get which Modules a Project may use: the Module Scopes assigned to it and to its Project Group
    /// </summary>
    [HttpGet("projects/{projectId:guid}/module-scopes")]
    [ProducesResponseType(typeof(ProjectModuleAccess), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetProjectModuleScopes")]
    public async Task<IActionResult> GetByProject([FromRoute] Guid projectId)
    {
        return Ok(await mediator.Send(new GetByProject.Query { ProjectId = projectId }));
    }
}
