// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Caster.Api.Features.Resources
{
    [Route("api/")]
    [ApiController]
    [Authorize]
    public class ResourcesController : ControllerBase
    {
        private readonly IMediator _mediator;

        public ResourcesController(IMediator mediator)
        {
            _mediator = mediator;
        }

        /// <summary>
        /// Get a single resource in a Workspace.
        /// </summary>
        [HttpGet("workspaces/{workspaceId}/resources/{address}")]
        [ProducesResponseType(typeof(Resource), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetResource")]
        public async Task<IActionResult> Get([FromRoute] Guid workspaceId, [FromRoute] string address, [FromQuery] Get.Query query)
        {
            query.WorkspaceId = workspaceId;
            query.Address = address;
            var result = await _mediator.Send(query);
            return Ok(result);
        }

        /// <summary>
        /// Get all resources in a Workspace.
        /// </summary>
        [HttpGet("workspaces/{workspaceId}/resources")]
        [ProducesResponseType(typeof(Resource[]), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetResourcesByWorkspace")]
        public async Task<IActionResult> GetByWorkspace([FromRoute] Guid workspaceId)
        {
            var result = await _mediator.Send(new GetByWorkspace.Query { WorkspaceId = workspaceId });
            return Ok(result);
        }

        /// <summary>
        /// Taint selected Resources
        /// </summary>
        [HttpPost("workspaces/{workspaceId}/resources/actions/taint")]
        [ProducesResponseType(typeof(ResourceCommandResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "TaintResources")]
        public async Task<IActionResult> Taint([FromRoute] Guid workspaceId, [FromBody] Taint.Command command)
        {
            command.WorkspaceId = workspaceId;
            var result = await _mediator.Send(command);
            return Ok(result);
        }

        /// <summary>
        /// Untaint selected Resources
        /// </summary>
        [HttpPost("workspaces/{workspaceId}/resources/actions/untaint")]
        [ProducesResponseType(typeof(ResourceCommandResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "UntaintResources")]
        public async Task<IActionResult> Untaint([FromRoute] Guid workspaceId, [FromBody] Taint.Command command)
        {
            command.WorkspaceId = workspaceId;
            command.Untaint = true;
            var result = await _mediator.Send(command);
            return Ok(result);
        }

        /// <summary>
        /// Remove selected Resources
        /// </summary>
        [HttpPost("workspaces/{workspaceId}/resources/actions/remove")]
        [ProducesResponseType(typeof(ResourceCommandResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "RemoveResources")]
        public async Task<IActionResult> Remove([FromRoute] Guid workspaceId, [FromBody] Remove.Command command)
        {
            command.WorkspaceId = workspaceId;
            var result = await _mediator.Send(command);
            return Ok(result);
        }

        /// <summary>
        /// Import a Resource
        /// </summary>
        [HttpPost("workspaces/{workspaceId}/resources/actions/import")]
        [ProducesResponseType(typeof(ResourceCommandResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "ImportResources")]
        public async Task<IActionResult> Import([FromRoute] Guid workspaceId, [FromBody] Import.Command command)
        {
            command.WorkspaceId = workspaceId;
            var result = await _mediator.Send(command);
            return Ok(result);
        }

        /// <summary>
        /// Refresh the Workspace's Resources
        /// </summary>
        [HttpPost("workspaces/{workspaceId}/resources/actions/refresh")]
        [ProducesResponseType(typeof(ResourceCommandResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "RefreshResources")]
        public async Task<IActionResult> Refresh([FromRoute] Guid workspaceId)
        {
            var result = await _mediator.Send(new Refresh.Command { WorkspaceId = workspaceId });
            return Ok(result);
        }

        /// <summary>
        /// Get the Workspace's Outputs
        /// </summary>
        [HttpPost("workspaces/{workspaceId}/resources/actions/outputs")]
        [ProducesResponseType(typeof(ResourceCommandResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetWorkspaceOutputs")]
        public async Task<IActionResult> Output([FromRoute] Guid workspaceId)
        {
            var result = await _mediator.Send(new Output.Command { WorkspaceId = workspaceId });
            return Ok(result);
        }
    }
}
