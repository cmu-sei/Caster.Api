// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Caster.Api.Features.Designs;

[Route("api/")]
[ApiController]
[Authorize]
public class DesignsController : ControllerBase
{
    private readonly IMediator _mediator;

    public DesignsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get a Design by Id
    /// </summary>
    /// <param name="id">The Id of the Design to retrieve</param>
    /// <param name="query"></param>
    [HttpGet("designs/{id}")]
    [ProducesResponseType(typeof(Design), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetDesign")]
    public async Task<IActionResult> Get([FromRoute] Guid id, [FromQuery] Get.Query query)
    {
        query.Id = id;
        var result = await this._mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Get all Designs within a Directory.
    /// </summary>
    /// <param name="directoryId">ID of a Directory.</param>
    /// <returns></returns>
    [HttpGet("directories/{directoryId}/designs")]
    [ProducesResponseType(typeof(IEnumerable<Design>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetDesignsByDirectory")]
    public async Task<IActionResult> GetByDirectory([FromRoute] Guid directoryId)
    {
        var result = await _mediator.Send(new GetByDirectory.Query { DirectoryId = directoryId });
        return Ok(result);
    }

    /// <summary>
    /// Creates a new Design
    /// </summary>
    /// <param name="command"></param>
    /// <returns></returns>
    [HttpPost("designs")]
    [ProducesResponseType(typeof(Design), (int)HttpStatusCode.Created)]
    [SwaggerOperation(OperationId = "CreateDesign")]
    public async Task<IActionResult> Create([FromBody] Create.Command command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    /// <summary>
    /// Update a Design
    /// </summary>
    /// <param name="id">Id of the Design to update</param>
    /// <param name="command"></param>
    /// <returns></returns>
    [HttpPut("designs/{id}")]
    [ProducesResponseType(typeof(Design), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "EditDesign")]
    public async Task<IActionResult> Edit([FromRoute] Guid id, [FromBody] Edit.Command command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Enable a Design
    /// </summary>
    /// <param name="id">Id of the Design to enable</param>
    /// <returns></returns>
    [HttpPost("designs/{id}/actions/enable")]
    [ProducesResponseType(typeof(Design), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "EnableDesign")]
    public async Task<IActionResult> Enable([FromRoute] Guid id)
    {
        var result = await _mediator.Send(new SetEnabled.Command
        {
            Id = id,
            Enabled = true
        });
        return Ok(result);
    }

    /// <summary>
    /// Disable a Design
    /// </summary>
    /// <param name="id">Id of the Design to disable</param>
    /// <returns></returns>
    [HttpPost("designs/{id}/actions/disable")]
    [ProducesResponseType(typeof(Design), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "DisableDesign")]
    public async Task<IActionResult> Disable([FromRoute] Guid id)
    {
        var result = await _mediator.Send(new SetEnabled.Command
        {
            Id = id,
            Enabled = false
        });
        return Ok(result);
    }

    /// <summary>
    /// Delete a design.
    /// </summary>
    /// <param name="id">ID of a design.</param>
    /// <returns></returns>
    [HttpDelete("designs/{id}")]
    [ProducesResponseType((int)HttpStatusCode.NoContent)]
    [SwaggerOperation(OperationId = "DeleteDesign")]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        await _mediator.Send(new Delete.Command { Id = id });
        return NoContent();
    }
}

