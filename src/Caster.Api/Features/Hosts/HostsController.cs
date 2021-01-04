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

namespace Caster.Api.Features.Hosts
{
    [Route("api/hosts")]
    [ApiController]
    [Authorize]
    public class HostsController : ControllerBase
    {
        private readonly IMediator _mediator;

        public HostsController(IMediator mediator) 
        {
            _mediator = mediator;
        }
        
        /// <summary>
        /// Get a single Host.
        /// </summary>
        /// <param name="id">ID of an Host.</param>
        /// <returns></returns>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(Host), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetHost")]
        public async Task<IActionResult> Get([FromRoute] Guid id)
        {
            var result = await _mediator.Send(new Get.Query { Id = id });
            return Ok(result);
        }
        
        /// <summary>
        /// Get all Hosts.
        /// </summary>
        /// <returns></returns>
        [HttpGet()]
        [ProducesResponseType(typeof(IEnumerable<Host>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetAllHosts")]
        public async Task<IActionResult> GetAll()
        {
            var result = await _mediator.Send(new GetAll.Query());
            return Ok(result);
        }

        /// <summary>
        /// Create a new Host.
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        [HttpPost()]
        [ProducesResponseType(typeof(Host), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "CreateHost")]
        public async Task<IActionResult> Create(Create.Command command)
        {
            var result = await _mediator.Send(command);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        }

        /// <summary>
        /// Update an Host.
        /// </summary>
        /// <param name="id">ID of an Host.</param>
        /// <param name="command"></param>
        /// <returns></returns>
        [HttpPut("{id}")]
        [ProducesResponseType(typeof(Host), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "EditHost")]
        public async Task<IActionResult> Edit([FromRoute] Guid id, Edit.Command command)
        {
            command.Id = id;
            var result = await _mediator.Send(command);
            return Ok(result);
        }
        
        /// <summary>
        /// Delete an Host.
        /// </summary>
        /// <param name="id">ID of an Host.</param>
        /// <returns></returns>
        [HttpDelete("{id}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "DeleteHost")]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            await _mediator.Send(new Delete.Command { Id = id });
            return NoContent();
        }
    }
}

