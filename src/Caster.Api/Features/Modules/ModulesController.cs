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

namespace Caster.Api.Features.Modules
{
    [Route("api/")]
    [ApiController]
    [Authorize]
    public class ModulesController : ControllerBase
    {
        private readonly IMediator _mediator;

        public ModulesController(IMediator mediator) 
        {
            _mediator = mediator;
        }
        
        /// <summary>
        /// Get a Module by Id
        /// </summary>
        /// <param name="id">The Id of the Module to retrieve</param>
        /// <param name="projectId">The project the Module is looked up for: a Module outside the project's Module Scopes is not found, unless a Design of the project uses it</param>
        [HttpGet("modules/{id}")]
        [ProducesResponseType(typeof(Module), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetModule")]
        public async Task<IActionResult> Get([FromRoute] string id, [FromQuery] Guid? projectId)
        {
            var result = await this._mediator.Send(new Get.Query { Id = Guid.Parse(id), ProjectId = projectId });
            return Ok(result);
        }

        /// <summary>
        /// Get all available Modules
        /// </summary>
        /// <remarks>
        /// Limited by Module Scopes: with a projectId, to what that project may use; without one, a
        /// holder of ViewModules gets every Module and anyone else what their own projects may use.
        /// </remarks>
        /// <param name="query"></param>
        [HttpGet("modules")]
        [ProducesResponseType(typeof(IEnumerable<Module>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetAllModules")]
        public async Task<IActionResult> GetAll([FromQuery] GetAll.Query query)
        {
            var result = await this._mediator.Send(query);
            return Ok(result);
        }

        /// <summary>
        /// Get every configured module source and how its last sync went
        /// </summary>
        /// <remarks>
        /// Listing Modules serves the stored Modules when a source fails to
        /// sync; this is where that failure is reported.
        /// </remarks>
        [HttpGet("modules/sources")]
        [ProducesResponseType(typeof(IEnumerable<ModuleSource>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetModuleSources")]
        public async Task<IActionResult> GetSources()
        {
            var result = await this._mediator.Send(new GetSources.Query());
            return Ok(result);
        }

        /// <summary>
        /// Create a new terraform module.
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        [HttpPost("modules")]
        [ProducesResponseType(typeof(Module), (int)HttpStatusCode.Created)]
        [SwaggerOperation(OperationId = "CreateTerrraformModule")]
        public async Task<IActionResult> Create(Create.Command command)
        {
            var result = await _mediator.Send(command);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        }

        /// <summary>
        /// Create or update a terraform module from the repository.
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        [HttpPost("repository/modules")]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "CreateTerrraformModuleFromRepository")]
        public async Task<IActionResult> CreateFromRepository(CreateFromRepository.Command command)
        {
            var result = await _mediator.Send(command);
            return Ok();
        }

        /// <summary>
        /// Create a terraform module snippet.
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        [HttpPost("modules/snippet")]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "CreateSnippet")]
        public async Task<IActionResult> CreateSnippet(CreateSnippet.Command command)
        {
            var result = await _mediator.Send(command);
            return Ok(result);
        }

        /// <summary>
        /// Delete a module.
        /// </summary>
        /// <param name="id">ID of a module.</param>
        /// <returns></returns>
        [HttpDelete("modules/{id}")]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        [SwaggerOperation(OperationId = "DeleteModule")]
        public async Task<IActionResult> Delete([FromRoute] string id)
        {
            await _mediator.Send(new Delete.Command { Id = Guid.Parse(id) });
            return NoContent();
        }

        /// <summary>
        /// Get a Module's Versions
        /// </summary>
        /// <param name="id">The Id of the Module to retrieve</param>
        /// <param name="projectId">The project the Module is looked up for: see Get a Module by Id</param>
        [HttpGet("modules/{id}/versions")]
        [ProducesResponseType(typeof(IEnumerable<ModuleVersion>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetModuleVersions")]
        public async Task<IActionResult> GetModuleVersions([FromRoute] string id, [FromQuery] Guid? projectId)
        {
            var result = await this._mediator.Send(new GetVersions.Query { ModuleId = Guid.Parse(id), ProjectId = projectId });
            return Ok(result);
        }

    }

}

