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

namespace Caster.Api.Features.Directories
{
    [Route("api/")]
    [ApiController]
    [Authorize]
    public class DirectoriesController : ControllerBase
    {
        private readonly IMediator _mediator;

        public DirectoriesController(IMediator mediator)
        {
            _mediator = mediator;
        }

        /// <summary>
        /// Retrieve a single directory.
        /// </summary>
        /// <param name="id">ID of a directory.</param>
        /// <param name="query"></param>
        /// <returns></returns>
        [HttpGet("directories/{id}")]
        [ProducesResponseType(typeof(Directory), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetDirectory")]
        public async Task<IActionResult> Get([FromRoute] Guid id, [FromQuery] Get.Query query)
        {
            query.Id = id;
            var result = await _mediator.Send(query);
            return Ok(result);
        }

        /// <summary>
        /// Export a directory.
        /// </summary>
        /// <param name="id">ID of a directory.</param>
        /// <param name="query"></param>
        /// <returns></returns>
        [HttpGet("directories/{id}/actions/export")]
        [ProducesResponseType(typeof(FileResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "ExportDirectory")]
        public async Task<IActionResult> Export([FromRoute] Guid id, [FromQuery] Export.Query query)
        {
            query.Id = id;
            var result = await _mediator.Send(query);
            return File(result.Data, result.Type, result.Name);
        }

        /// <summary>
        /// Import a directory.
        /// </summary>
        /// <param name="id">ID of a directory.</param>
        /// <param name="command"></param>
        [HttpPost("directories/{id}/actions/import")]
        [ProducesResponseType(typeof(Import.ImportDirectoryResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "ImportDirectory")]
        public async Task<IActionResult> Import([FromRoute] Guid id, [FromQuery] Import.Command command)
        {
            command.Id = id;
            var result = await _mediator.Send(command);
            return Ok(result);
        }

        /// <summary>
        /// Retrieve all directories.
        /// </summary>
        /// <returns></returns>
        [HttpGet("directories")]
        [ProducesResponseType(typeof(IEnumerable<Directory>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetAllDirectories")]
        public async Task<IActionResult> GetAll([FromQuery] GetAll.Query query)
        {
            var result = await _mediator.Send(query);
            return Ok(result);
        }

        /// <summary>
        /// Retrieve all directories within a single project.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="query"></param>
        /// <returns></returns>
        [HttpGet("projects/{projectId}/directories")]
        [ProducesResponseType(typeof(IEnumerable<Directory>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetDirectoriesByProject")]
        public async Task<IActionResult> GetByProject([FromRoute] Guid projectId, [FromQuery] GetByProject.Query query)
        {
            query.ProjectId = projectId;
            var result = await _mediator.Send(query);
            return Ok(result);
        }

        /// <summary>
        /// Retrieve all directories for the current user.
        /// </summary>
        /// <returns></returns>
        [HttpGet("directories/mine")]
        [ProducesResponseType(typeof(IEnumerable<Directory>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetMyDirectories")]
        public async Task<IActionResult> GetMine([FromQuery] GetMine.Query query)
        {
            var result = await _mediator.Send(query);
            return Ok(result);
        }

        /// <summary>
        /// Retrieve all directories that are children of a specified directory
        /// </summary>
        /// <param name="directoryId"></param>
        /// <param name="query"></param>
        /// <returns></returns>
        [HttpGet("directories/{directoryId}/children")]
        [ProducesResponseType(typeof(IEnumerable<Directory>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetDirectoryChildren")]
        public async Task<IActionResult> GetChildren([FromRoute] Guid directoryId, [FromQuery] GetChildren.Query query)
        {
            query.DirectoryId = directoryId;
            var result = await _mediator.Send(query);
            return Ok(result);
        }

        /// <summary>
        /// Create a new directory.
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        [HttpPost("directories")]
        [ProducesResponseType(typeof(Directory), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "CreateDirectory")]
        public async Task<IActionResult> Create(Create.Command command)
        {
            var result = await _mediator.Send(command);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        }

        /// <summary>
        /// Update a directory.
        /// </summary>
        /// <param name="id">ID of a directory.</param>
        /// <param name="command"></param>
        /// <returns></returns>
        [HttpPut("directories/{id}")]
        [ProducesResponseType(typeof(Directory), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "EditDirectory")]
        public async Task<IActionResult> Edit([FromRoute] Guid id, Edit.Command command)
        {
            command.Id = id;
            var result = await _mediator.Send(command);
            return Ok(result);
        }

        /// <summary>
        /// Partial update a directory.
        /// </summary>
        /// <param name="id">ID of a directory.</param>
        /// <param name="command"></param>
        /// <returns></returns>
        [HttpPatch("directories/{id}")]
        [ProducesResponseType(typeof(Directory), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "PartialEditDirectory")]
        public async Task<IActionResult> PartialEdit([FromRoute] Guid id, PartialEdit.Command command)
        {
            command.Id = id;
            var result = await _mediator.Send(command);
            return Ok(result);
        }

        /// <summary>
        /// Delete a directory.
        /// </summary>
        /// <param name="id">ID of a directory.</param>
        /// <returns></returns>
        [HttpDelete("directories/{id}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "DeleteDirectory")]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            await _mediator.Send(new Delete.Command { Id = id });
            return NoContent();
        }
    }
}
