// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Caster.Api.Features.Workspaces
{
    [Route("api/")]
    [ApiController]
    [Authorize]
    public class WorkspacesController : ControllerBase
    {
        private readonly IMediator _mediator;

        public WorkspacesController(IMediator mediator)
        {
            this._mediator = mediator;
        }

        /// <summary>
        /// Get a Workspace by Id
        /// </summary>
        /// <param name="id">The Id of the Workspace to retrieve</param>
        [HttpGet("workspaces/{id}")]
        [ProducesResponseType(typeof(Workspace), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetWorkspace")]
        public async Task<IActionResult> Get([FromRoute] Guid id)
        {
            var result = await this._mediator.Send(new Get.Query { Id = id });
            return Ok(result);
        }

        /// <summary>
        /// Get all Workspaces
        /// </summary>
        [HttpGet("workspaces")]
        [ProducesResponseType(typeof(Workspace[]), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetWorkspaces")]
        public async Task<IActionResult> GetAll()
        {
            var result = await this._mediator.Send(new GetAll.Query());
            return Ok(result);
        }

        /// <summary>
        /// Get all Workspaces for a Directory
        /// </summary>
        /// <param name="directoryId">The Id of the Directory</param>
        [HttpGet("directories/{directoryId}/workspaces")]
        [ProducesResponseType(typeof(Workspace[]), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetWorkspacesByProjectId")]
        public async Task<IActionResult> GetByProject([FromRoute] Guid directoryId)
        {
            var result = await this._mediator.Send(new GetByDirectory.Query { DirectoryId = directoryId });
            return Ok(result);
        }

        /// <summary>
        /// Create a Workspace
        /// </summary>
        /// <param name="command">The Create command</param>
        [HttpPost("workspaces")]
        [ProducesResponseType(typeof(Workspace), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "CreateWorkspace")]
        public async Task<IActionResult> Create(Create.Command command)
        {
            var result = await this._mediator.Send(command);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        }

        /// <summary>
        /// Edit a Workspace
        /// </summary>
        /// <param name="id">The Id of the Workspace to Edit</param>
        /// <param name="command">The Edit command</param>
        /// <returns></returns>
        [HttpPut("workspaces/{id}")]
        [ProducesResponseType(typeof(Workspace), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "EditWorkspace")]
        public async Task<IActionResult> Edit([FromRoute] Guid id, Edit.Command command)
        {
            command.Id = id;
            var result = await this._mediator.Send(command);
            return Ok(result);
        }

        /// <summary>
        /// Partial Edit a Workspace
        /// </summary>
        /// <param name="id">The Id of the Workspace to Edit</param>
        /// <param name="command">The PartialEdit command</param>
        [HttpPatch("workspaces/{id}")]
        [ProducesResponseType(typeof(Workspace), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "PartialEditWorkspace")]
        public async Task<IActionResult> PartialEdit([FromRoute] Guid id, PartialEdit.Command command)
        {
            command.Id = id;
            var result = await this._mediator.Send(command);
            return Ok(result);
        }

        /// <summary>
        /// Delete a Workspace
        /// </summary>
        /// <param name="id">The Id of the Workspace to Delete</param>
        [HttpDelete("workspaces/{id}")]
        [ProducesResponseType(typeof(Workspace), (int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "DeleteWorkspace")]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            await this._mediator.Send(new Delete.Command { Id = id });
            return NoContent();
        }

        /// <summary>
        /// Get the value of the global Workspaces lock status
        /// </summary>
        [HttpGet("workspaces/locking-status")]
        [ProducesResponseType(typeof(bool), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetWorkspaceLockingStatus")]
        public async Task<IActionResult> GetLockingStatus()
        {
            var result = await this._mediator.Send(new GetLockingStatus.Query());
            return Ok(result);
        }

        /// <summary>
        /// Enable Workspace locking globally. Can only be accessed by a System Administrator.
        /// </summary>
        [HttpPost("workspaces/actions/enable-locking")]
        [ProducesResponseType(typeof(bool), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "EnableWorkspaceLocking")]
        public async Task<IActionResult> EnableLocking()
        {
            var result = await this._mediator.Send(new SetLockingStatus.Command { Enabled = true });
            return Ok(result);
        }

        /// <summary>
        /// Disable Workspace locking globally. Can only be accessed by a System Administrator.
        /// Use before taking the application down for maintenance and ensure no Workspace operations are in progress.
        /// </summary>
        [HttpPost("workspaces/actions/disable-locking")]
        [ProducesResponseType(typeof(bool), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "DisableWorkspaceLocking")]
        public async Task<IActionResult> DisableLocking()
        {
            var result = await this._mediator.Send(new SetLockingStatus.Command { Enabled = false });
            return Ok(result);
        }
    }
}
