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

namespace Caster.Api.Features.SystemRoles
{
    [Route("api/system-roles")]
    [ApiController]
    [Authorize]
    public class SystemRolesController : ControllerBase
    {
        private readonly IMediator _mediator;

        public SystemRolesController(IMediator mediator)
        {
            _mediator = mediator;
        }

        /// <summary>
        /// Get a single SystemRole.
        /// </summary>
        /// <param name="id">ID of an SystemRole.</param>
        /// <returns></returns>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(SystemRole), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetSystemRole")]
        public async Task<IActionResult> Get([FromRoute] Guid id)
        {
            var result = await _mediator.Send(new Get.Query { Id = id });
            return Ok(result);
        }

        /// <summary>
        /// Get all SystemRoles.
        /// </summary>
        /// <returns></returns>
        [HttpGet()]
        [ProducesResponseType(typeof(IEnumerable<SystemRole>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetAllSystemRoles")]
        public async Task<IActionResult> GetAll()
        {
            var result = await _mediator.Send(new GetAll.Query());
            return Ok(result);
        }

        /// <summary>
        /// Create a new SystemRole.
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        [HttpPost()]
        [ProducesResponseType(typeof(SystemRole), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "CreateSystemRole")]
        public async Task<IActionResult> Create(Create.Command command)
        {
            var result = await _mediator.Send(command);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        }

        /// <summary>
        /// Update a SystemRole.
        /// </summary>
        /// <param name="id">ID of an SystemRole.</param>
        /// <param name="command"></param>
        /// <returns></returns>
        [HttpPut("{id}")]
        [ProducesResponseType(typeof(SystemRole), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "EditSystemRole")]
        public async Task<IActionResult> Edit([FromRoute] Guid id, Edit.Command command)
        {
            command.Id = id;
            var result = await _mediator.Send(command);
            return Ok(result);
        }

        /// <summary>
        /// Delete a SystemRole.
        /// </summary>
        /// <param name="id">ID of an SystemRole.</param>
        /// <returns></returns>
        [HttpDelete("{id}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "DeleteSystemRole")]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            await _mediator.Send(new Delete.Command { Id = id });
            return NoContent();
        }
    }
}

