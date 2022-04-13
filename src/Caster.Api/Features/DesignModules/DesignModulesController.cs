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

namespace Caster.Api.Features.DesignModules;

[Route("api/")]
[ApiController]
[Authorize]
public class DesignsModulesController : ControllerBase
{
    private readonly IMediator _mediator;

    public DesignsModulesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get a DesignModule by Id
    /// </summary>
    /// <param name="id">The Id of the DesignModule to retrieve</param>
    /// <param name="query"></param>
    [HttpGet("designModules/{id}")]
    [ProducesResponseType(typeof(DesignModule), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetDesignModule")]
    public async Task<IActionResult> Get([FromRoute] Guid id, [FromQuery] Get.Query query)
    {
        query.Id = id;
        var result = await this._mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Get all DesignModules within a Design.
    /// </summary>
    /// <param name="designId">ID of a Design.</param>
    /// <param name="query"></param>
    /// <returns></returns>
    [HttpGet("designs/{designId}/modules")]
    [ProducesResponseType(typeof(IEnumerable<DesignModule>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetDesignModulesByDesign")]
    public async Task<IActionResult> GetByDirectory([FromRoute] Guid designId, [FromQuery] GetByDesign.Query query)
    {
        query.DesignId = designId;
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Creates a new DesignModule
    /// </summary>
    /// <param name="command"></param>
    /// <returns></returns>
    [HttpPost("designModules")]
    [ProducesResponseType(typeof(DesignModule), (int)HttpStatusCode.Created)]
    [SwaggerOperation(OperationId = "CreateDesignModule")]
    public async Task<IActionResult> Create([FromBody] Create.Command command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    /// <summary>
    /// Update a DesignModule
    /// </summary>
    /// <param name="id">The Id of the DesignModule to edit</param>
    /// <param name="command"></param>
    /// <returns></returns>
    [HttpPut("designModules/{id}")]
    [ProducesResponseType(typeof(DesignModule), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "EditDesignModule")]
    public async Task<IActionResult> Edit([FromRoute] Guid id, [FromBody] Edit.Command command)
    {
        command.DesignModuleId = id;
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Enable a DesignModule
    /// </summary>
    /// <param name="id">The Id of the DesignModule to enable</param>
    /// <returns></returns>
    [HttpPost("designModules/{id}/actions/enable")]
    [ProducesResponseType(typeof(DesignModule), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "EnableDesignModule")]
    public async Task<IActionResult> Enable([FromRoute] Guid id)
    {
        var result = await _mediator.Send(new SetEnabled.Command
        {
            DesignModuleId = id,
            Enabled = true
        });

        return Ok(result);
    }

    /// <summary>
    /// Disable a DesignModule
    /// </summary>
    /// <param name="id">The Id of the DesignModule to disable</param>
    /// <returns></returns>
    [HttpPost("designModules/{id}/actions/disable")]
    [ProducesResponseType(typeof(DesignModule), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "DisableDesignModule")]
    public async Task<IActionResult> Disable([FromRoute] Guid id)
    {
        var result = await _mediator.Send(new SetEnabled.Command
        {
            DesignModuleId = id,
            Enabled = false
        });

        return Ok(result);
    }

    /// <summary>
    /// Add or Update values for a DesignModule
    /// </summary>
    /// <param name="designModuleId">The Id of the DesignModule to set values for</param>
    /// <param name="command"></param>
    /// <returns></returns>
    [HttpPost("designModules/{designModuleId}/values")]
    [ProducesResponseType(typeof(DesignModule), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "AddOrUpdateValuesDesignModule")]
    public async Task<IActionResult> AddOrUpdateValues([FromRoute] Guid designModuleId, [FromBody] AddOrUpdateValues.Command command)
    {
        command.DesignModuleId = designModuleId;
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Delete a designModule.
    /// </summary>
    /// <param name="id">ID of a designModule</param>
    /// <returns></returns>
    [HttpDelete("designsModules/{id}")]
    [ProducesResponseType((int)HttpStatusCode.NoContent)]
    [SwaggerOperation(OperationId = "DeleteDesignModule")]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        await _mediator.Send(new Delete.Command { Id = id });
        return NoContent();
    }
}

