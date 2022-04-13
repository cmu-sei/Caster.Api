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

namespace Caster.Api.Features.Variables;

[Route("api/")]
[ApiController]
[Authorize]
public class VariablesController : ControllerBase
{
    private readonly IMediator _mediator;

    public VariablesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get a Variable by Id
    /// </summary>
    /// <param name="id">The Id of the Variable to retrieve</param>
    /// <param name="query"></param>
    [HttpGet("variables/{id}")]
    [ProducesResponseType(typeof(Variable), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetVariable")]
    public async Task<IActionResult> Get([FromRoute] Guid id, [FromQuery] Get.Query query)
    {
        query.Id = id;
        var result = await this._mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Get all Variables within a Design.
    /// </summary>
    /// <param name="designId">ID of a Design.</param>
    /// <returns></returns>
    [HttpGet("designss/{designId}/variables")]
    [ProducesResponseType(typeof(IEnumerable<Variable>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetVariablesByDesign")]
    public async Task<IActionResult> GetByDirectory([FromRoute] Guid designId)
    {
        var result = await _mediator.Send(new GetAll.Query { DesignId = designId });
        return Ok(result);
    }

    /// <summary>
    /// Creates a new Variable
    /// </summary>
    /// <param name="command"></param>
    /// <returns></returns>
    [HttpPost("variables")]
    [ProducesResponseType(typeof(Variable), (int)HttpStatusCode.Created)]
    [SwaggerOperation(OperationId = "CreateVariable")]
    public async Task<IActionResult> Create([FromBody] Create.Command command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    /// <summary>
    /// Update a Variable
    /// </summary>
    /// <param name="id">Id of the Variable to update</param>
    /// <param name="command"></param>
    /// <returns></returns>
    [HttpPut("variables/{id}")]
    [ProducesResponseType(typeof(Variable), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "EditVariable")]
    public async Task<IActionResult> Edit([FromRoute] Guid id, [FromBody] Edit.Command command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Delete a Variable.
    /// </summary>
    /// <param name="id">ID of a Variable.</param>
    /// <returns></returns>
    [HttpDelete("variables/{id}")]
    [ProducesResponseType((int)HttpStatusCode.NoContent)]
    [SwaggerOperation(OperationId = "DeleteVariable")]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        await _mediator.Send(new Delete.Command { Id = id });
        return NoContent();
    }
}

