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

namespace Caster.Api.Features.Permissions
{
    [Route("api/permissions")]
    [ApiController]
    [Authorize]
    public class PermissionsController : ControllerBase
    {
        private readonly IMediator _mediator;

        public PermissionsController(IMediator mediator) 
        {
            _mediator = mediator;
        }
        
        /// <summary>
        /// Get a single permission.
        /// </summary>
        /// <param name="id">ID of an permission.</param>
        /// <returns></returns>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(Permission), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetPermission")]
        public async Task<IActionResult> Get([FromRoute] Guid id)
        {
            var result = await _mediator.Send(new Get.Query { Id = id });
            return Ok(result);
        }

        /// <summary>
        /// Get all permissions.
        /// </summary>
        /// <returns></returns>
        [HttpGet()]
        [ProducesResponseType(typeof(IEnumerable<Permission>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetAllPermissions")]
        public async Task<IActionResult> GetAll()
        {
            var result = await _mediator.Send(new GetAll.Query());
            return Ok(result);
        }

        /// <summary>
        /// Get permissions for the current user.
        /// </summary>
        /// <returns></returns>
        [HttpGet("mine")]
        [ProducesResponseType(typeof(IEnumerable<Permission>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetMyPermissions")]
        public async Task<IActionResult> GetMine()
        {
            var result = await _mediator.Send(new GetMine.Query());
            return Ok(result);
        }

        /// <summary>
        /// Get permissions for a user.
        /// </summary>
        /// <param name="userId">ID of a user.</param>
        /// <returns></returns>
        [HttpGet("/users/{userId}/permissions")]
        [ProducesResponseType(typeof(IEnumerable<Permission>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetPermissionsByUser")]
        public async Task<IActionResult> GetByUser([FromRoute] Guid userId)
        {
            var result = await _mediator.Send(new GetPermissionsByUser.Query() { UserId = userId });
            return Ok(result);
        }
        
    }
}

