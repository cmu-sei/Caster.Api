// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using Swashbuckle.AspNetCore.Annotations;

namespace Caster.Api.Features.Groups
{
    [Route("api/groups")]
    [ApiController]
    [Authorize]
    public class GroupsController : ControllerBase
    {
        private readonly IMediator _mediator;

        public GroupsController(IMediator mediator)
        {
            _mediator = mediator;
        }

        /// <summary>
        /// Get a single group.
        /// </summary>
        /// <param name="id">ID of an group.</param>
        /// <returns></returns>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(Group), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetGroup")]
        public async Task<IActionResult> Get([FromRoute] Guid id)
        {
            var result = await _mediator.Send(new Get.Query { Id = id });
            return Ok(result);
        }

        /// <summary>
        /// Get all groups.
        /// </summary>
        /// <returns></returns>
        [HttpGet()]
        [ProducesResponseType(typeof(IEnumerable<Group>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetAllGroups")]
        public async Task<IActionResult> GetAll()
        {
            var result = await _mediator.Send(new GetAll.Query());
            return Ok(result);
        }

        /// <summary>
        /// Create a new group.
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        [HttpPost()]
        [ProducesResponseType(typeof(Group), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "CreateGroup")]
        public async Task<IActionResult> Create(Create.Command command)
        {
            var result = await _mediator.Send(command);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        }

        /// <summary>
        /// Update a group.
        /// </summary>
        /// <returns></returns>
        [HttpPut("")]
        [ProducesResponseType(typeof(Group), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "EditGroup")]
        public async Task<IActionResult> Edit(Edit.Command command)
        {
            var result = await _mediator.Send(command);
            return Ok(result);
        }

        /// <summary>
        /// Delete a group.
        /// </summary>
        /// <param name="id">ID of an group.</param>
        /// <returns></returns>
        [HttpDelete("{id}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "DeleteGroup")]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            await _mediator.Send(new Delete.Command { Id = id });
            return NoContent();
        }

        /// <summary>
        /// Get a single Group Membership.
        /// </summary>
        /// <returns></returns>
        [HttpGet("membership/{id}")]
        [ProducesResponseType(typeof(GroupMembership), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetGroupMembership")]
        public async Task<IActionResult> GetMembership([FromRoute] Guid id)
        {
            var result = await _mediator.Send(new GetMembership.Query { Id = id });
            return Ok(result);
        }

        /// <summary>
        /// Get all Group Memberships of a Group.
        /// </summary>
        /// <returns></returns>
        [HttpGet("{groupId}/memberships")]
        [ProducesResponseType(typeof(IEnumerable<GroupMembership>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetGroupMemberships")]
        public async Task<IActionResult> GetMemberships([FromRoute] Guid groupId)
        {
            var result = await _mediator.Send(new GetMemberships.Query() { GroupId = groupId });
            return Ok(result);
        }

        /// <summary>
        /// Create a new Group Membership.
        /// </summary>
        /// <param name="groupId"></param>
        /// <param name="command"></param>
        /// <returns></returns>
        [HttpPost("{groupId}/memberships")]
        [ProducesResponseType(typeof(GroupMembership), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "CreateGroupMembership")]
        public async Task<IActionResult> CreateMembership([FromRoute] Guid groupId, CreateMembership.Command command)
        {
            command.GroupId = groupId;
            var result = await _mediator.Send(command);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        }

        /// <summary>
        /// Delete a Group Membership.
        /// </summary>
        /// <returns></returns>
        [HttpDelete("memberships/{id}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "DeleteGroupMembership")]
        public async Task<IActionResult> DeleteMembership([FromRoute] Guid id)
        {
            await _mediator.Send(new DeleteMembership.Command { Id = id });
            return NoContent();
        }
    }
}
