// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Caster.Api.Features.Applies
{
    [Route("api/")]
    [ApiController]
    [Authorize]
    public class AppliesController : ControllerBase
    {
        private readonly IMediator _mediator;

        public AppliesController(IMediator mediator)
        {
            _mediator = mediator;
        }

        /// <summary>
        /// Get a single Apply
        /// </summary>
        /// <param name="id">ID of an Apply</param>
        [HttpGet("applies/{id}")]
        [ProducesResponseType(typeof(Apply), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetApply")]
        public async Task<IActionResult> Get([FromRoute] Guid id)
        {
            var result = await _mediator.Send(new Get.Query { Id = id });
            return Ok(result);
        }

        /// <summary>
        /// Get a single Apply by Run Id
        /// </summary>
        /// <param name="runId">ID of a Run</param>
        [HttpGet("runs/{runId}/apply")]
        [ProducesResponseType(typeof(Apply), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetApplyByRunId")]
        public async Task<IActionResult> GetByRun([FromRoute] Guid runId)
        {
            var result = await _mediator.Send(new GetByRun.Query { RunId = runId });
            return Ok(result);
        }

        /// <summary>
        /// Applies the Plan associated with the specified Run
        /// </summary>
        /// <param name="runId"></param>
        [HttpPost("runs/{runId}/actions/apply")]
        [ProducesResponseType(typeof(Apply), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "ApplyRun")]
        public async Task<IActionResult> Execute([FromRoute] Guid runId)
        {
            var result = await _mediator.Send(new Execute.Command { RunId = runId });
            return Ok(result);
        }
    }
}
