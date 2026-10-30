// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Data;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using Swashbuckle.AspNetCore.Annotations;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Projects
{
    [Route("api/projects")]
    [ApiController]
    [Authorize]
    public class ProjectsController : ControllerBase
    {
        private readonly IMediator _mediator;

        public ProjectsController(IMediator mediator)
        {
            _mediator = mediator;
        }

        /// <summary>
        /// Get a single project.
        /// </summary>
        /// <param name="id">ID of an project.</param>
        /// <returns></returns>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(Project), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetProject")]
        public async Task<IActionResult> Get([FromRoute] Guid id)
        {
            var result = await _mediator.Send(new Get.Query { Id = id });
            return Ok(result);
        }

        /// <summary>
        /// Export a single project.
        /// </summary>
        /// <param name="id">ID of an project.</param>
        /// <param name="query"></param>
        /// <returns></returns>
        [HttpGet("{id}/actions/export")]
        [ProducesResponseType(typeof(FileResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "ExportProject")]
        public async Task<IActionResult> Export([FromRoute] Guid id, [FromQuery] Export.Query query)
        {
            query.Id = id;
            var result = await _mediator.Send(query);
            return File(result.Data, result.Type, result.Name);
        }

        /// <summary>
        /// Export a single project. Obsolete: use GET api/projects/{id}/actions/export.
        /// </summary>
        /// <param name="id">ID of an project.</param>
        /// <param name="query"></param>
        /// <returns></returns>
        [Obsolete("Use GET api/projects/{id}/actions/export.")]
        [HttpGet("projects/{id}/actions/export")]
        [ProducesResponseType(typeof(FileResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "ExportProjectObsolete")]
        public Task<IActionResult> ExportObsolete([FromRoute] Guid id, [FromQuery] Export.Query query)
        {
            return Export(id, query);
        }

        /// <summary>
        /// The VM templates and ISOs an export of a project would list in its disk manifest.
        /// </summary>
        /// <param name="id">ID of a project.</param>
        [HttpGet("{id}/disk-manifest")]
        [ProducesResponseType(typeof(DiskManifest), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetProjectDiskManifest")]
        public async Task<IActionResult> GetDiskManifest([FromRoute] Guid id)
        {
            var result = await _mediator.Send(new GetDiskManifest.Query { Id = id });
            return Ok(result);
        }

        /// <summary>
        /// What drafting with AI used in a project, and an estimate of what it cost.
        /// </summary>
        /// <remarks>
        /// Every call to an AI model made for a Directory of the project, in all and by task (design, terraform, brief).
        /// The estimate uses the prices in the Ai:Prices setting and leaves out the models it has no price for.
        /// </remarks>
        /// <param name="id">ID of a project.</param>
        /// <param name="cancellationToken"></param>
        [HttpGet("{id}/ai-usage")]
        [ProducesResponseType(typeof(ProjectAiUsage), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetProjectAiUsage")]
        public async Task<IActionResult> GetAiUsage([FromRoute] Guid id, CancellationToken cancellationToken)
        {
            var result = await _mediator.Send(new GetAiUsage.Query { Id = id }, cancellationToken);
            return Ok(result);
        }

        /// <summary>
        /// Import an project.
        /// </summary>
        /// <param name="id">ID of an project.</param>
        /// <param name="command"></param>
        [HttpPost("{id}/actions/import")]
        [ProducesResponseType(typeof(Import.ImportProjectResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "ImportProject")]
        public async Task<IActionResult> Import([FromRoute] Guid id, [FromQuery] Import.Command command)
        {
            command.Id = id;
            var result = await _mediator.Send(command);
            return Ok(result);
        }

        /// <summary>
        /// Import an project. Obsolete: use POST api/projects/{id}/actions/import.
        /// </summary>
        /// <param name="id">ID of an project.</param>
        /// <param name="command"></param>
        [Obsolete("Use POST api/projects/{id}/actions/import.")]
        [HttpPost("projects/{id}/actions/import")]
        [ProducesResponseType(typeof(Import.ImportProjectResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "ImportProjectObsolete")]
        public Task<IActionResult> ImportObsolete([FromRoute] Guid id, [FromQuery] Import.Command command)
        {
            return Import(id, command);
        }

        /// <summary>
        /// Get all projects.
        /// </summary>
        /// <returns></returns>
        [HttpGet()]
        [ProducesResponseType(typeof(IEnumerable<Project>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetAllProjects")]
        public async Task<IActionResult> GetAll([FromQuery] GetAll.Query query)
        {
            var result = await _mediator.Send(query);
            return Ok(result);
        }

        /// <summary>
        /// Create a new project.
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        [HttpPost()]
        [ProducesResponseType(typeof(Project), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "CreateProject")]
        public async Task<IActionResult> Create(Create.Command command)
        {
            var result = await _mediator.Send(command);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        }

        /// <summary>
        /// Update an project.
        /// </summary>
        /// <param name="id">ID of an project.</param>
        /// <param name="command"></param>
        /// <returns></returns>
        [HttpPut("{id}")]
        [ProducesResponseType(typeof(Project), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "EditProject")]
        public async Task<IActionResult> Edit([FromRoute] Guid id, Edit.Command command)
        {
            command.Id = id;
            var result = await _mediator.Send(command);
            return Ok(result);
        }

        /// <summary>
        /// Delete a project.
        /// </summary>
        /// <param name="id">ID of an project.</param>
        /// <returns></returns>
        [HttpDelete("{id}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "DeleteProject")]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            await _mediator.Send(new Delete.Command { Id = id });
            return NoContent();
        }

        /// <summary>
        /// Get a single Project Membership.
        /// </summary>
        /// <returns></returns>
        [HttpGet("memberships/{id}")]
        [ProducesResponseType(typeof(ProjectMembership), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetProjectMembership")]
        public async Task<IActionResult> GetMembership([FromRoute] Guid id)
        {
            var result = await _mediator.Send(new GetMembership.Query { Id = id });
            return Ok(result);
        }

        /// <summary>
        /// Get all Project Memberships of a Project.
        /// </summary>
        /// <returns></returns>
        [HttpGet("{projectId}/memberships")]
        [ProducesResponseType(typeof(IEnumerable<ProjectMembership>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetProjectMemberships")]
        public async Task<IActionResult> GetMemberships([FromRoute] Guid projectId)
        {
            var result = await _mediator.Send(new GetMemberships.Query() { ProjectId = projectId });
            return Ok(result);
        }

        /// <summary>
        /// Create a new Project Membership.
        /// </summary>
        /// <param name="projectId"></param>
        /// <param name="command"></param>
        /// <returns></returns>
        [HttpPost("{projectId}/memberships")]
        [ProducesResponseType(typeof(ProjectMembership), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "CreateProjectMembership")]
        public async Task<IActionResult> CreateMembership([FromRoute] Guid projectId, CreateMembership.Command command)
        {
            command.ProjectId = projectId;
            var result = await _mediator.Send(command);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        }

        /// <summary>
        /// Edit a Project Membership.
        /// </summary>
        /// <returns></returns>
        [HttpPut("memberships")]
        [ProducesResponseType(typeof(ProjectMembership), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "EditProjectMembership")]
        public async Task<IActionResult> EditMembership(EditMembership.Command command)
        {
            var result = await _mediator.Send(command);
            return Ok(result);
        }

        /// <summary>
        /// Delete a Project Membership.
        /// </summary>
        /// <returns></returns>
        [HttpDelete("memberships/{id}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "DeleteProjectMembership")]
        public async Task<IActionResult> DeleteMembership([FromRoute] Guid id)
        {
            await _mediator.Send(new DeleteMembership.Command { Id = id });
            return NoContent();
        }
    }
}
