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
        /// <remarks>
        /// Refused with a 409 when the run takes its workspace over an effective
        /// limit, or when the hypervisors may not have room for it and
        /// Capacity:RoomCheck is Block. With RoomCheck Warn, a run the
        /// hypervisors may not have room for is applied only when
        /// acknowledgeCapacityWarning is true. See GET runs/{runId}/capacity.
        /// </remarks>
        /// <param name="runId"></param>
        /// <param name="acknowledgeCapacityWarning">True to apply even though the hypervisors may not have room for the run.</param>
        [HttpPost("runs/{runId}/actions/apply")]
        [ProducesResponseType(typeof(Apply), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "ApplyRun")]
        public async Task<IActionResult> Execute([FromRoute] Guid runId, [FromQuery] bool acknowledgeCapacityWarning = false)
        {
            var result = await _mediator.Send(new Execute.Command { RunId = runId, AcknowledgeCapacityWarning = acknowledgeCapacityWarning });
            return Ok(result);
        }
    }
}
