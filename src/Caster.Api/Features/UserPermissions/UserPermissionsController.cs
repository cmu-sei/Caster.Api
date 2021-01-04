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

namespace Caster.Api.Features.UserPermissions
{
    [Route("api/userPermissions")]
    [ApiController]
    [Authorize]
    public class UserPermissionsController : ControllerBase
    {
        private readonly IMediator _mediator;

        public UserPermissionsController(IMediator mediator) 
        {
            _mediator = mediator;
        }
        
        /// <summary>
        /// Get a single userPermission.
        /// </summary>
        /// <param name="id">ID of an userPermission.</param>
        /// <returns></returns>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(UserPermission), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetUserPermission")]
        public async Task<IActionResult> Get([FromRoute] Guid id)
        {
            var result = await _mediator.Send(new Get.Query { Id = id });
            return Ok(result);
        }
        
        /// <summary>
        /// Get all userPermissions.
        /// </summary>
        /// <returns></returns>
        [HttpGet()]
        [ProducesResponseType(typeof(IEnumerable<UserPermission>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetAllUserPermissions")]
        public async Task<IActionResult> GetAll()
        {
            var result = await _mediator.Send(new GetAll.Query());
            return Ok(result);
        }

        /// <summary>
        /// Create a new userPermission.
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        [HttpPost()]
        [ProducesResponseType(typeof(UserPermission), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "CreateUserPermission")]
        public async Task<IActionResult> Create(Create.Command command)
        {
            var result = await _mediator.Send(command);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        }

        /// <summary>
        /// Update a userPermission.
        /// </summary>
        /// <param name="id">ID of an userPermission.</param>
        /// <param name="command"></param>
        /// <returns></returns>
        [HttpPut("{id}")]
        [ProducesResponseType(typeof(UserPermission), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "EditUserPermission")]
        public async Task<IActionResult> Edit([FromRoute] Guid id, Edit.Command command)
        {
            command.Id = id;
            var result = await _mediator.Send(command);
            return Ok(result);
        }
        
        /// <summary>
        /// Delete a userPermission.
        /// </summary>
        /// <param name="id">ID of an userPermission.</param>
        /// <returns></returns>
        [HttpDelete("{id}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "DeleteUserPermission")]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            await _mediator.Send(new Delete.Command { Id = id });
            return NoContent();
        }

        /// <summary>
        /// Delete a userPermission by user and permission.
        /// </summary>
        /// <param name="userId">ID of a user.</param>
        /// <param name="permissionId">ID of a permission.</param>
        /// <returns></returns>
        [HttpDelete("/api/users/{userId}/permissions/{permissionId}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "DeleteUserPermissionByIds")]
        public async Task<IActionResult> DeleteByIds([FromRoute] Guid userId, [FromRoute] Guid permissionId)
        {
            await _mediator.Send(new Delete.Command { UserId = userId, PermissionId = permissionId });
            return NoContent();
        }

    }
}

