// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Caster.Api.Features.Plans
{
    [Route("api/")]
    [ApiController]
    [Authorize]
    public class PlansController : ControllerBase
    {
        private readonly IMediator _mediator;

        public PlansController(IMediator mediator) 
        {
            _mediator = mediator;
        }
        
        /// <summary>
        /// Get a Plan by Id
        /// </summary>
        /// <param name="id">The Id of the Plan to retrieve</param>
        [HttpGet("plans/{id}")]
        [ProducesResponseType(typeof(Plan), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetPlan")]
        public async Task<IActionResult> Get([FromRoute] Guid id)
        {
            var result = await this._mediator.Send(new Get.Query { Id = id });
            return Ok(result);
        }

        /// <summary>
        /// Get a Plan by Run Id
        /// </summary>
        /// <param name="runId">The Id of the Run</param>
        [HttpGet("runs/{runId}/plan")]
        [ProducesResponseType(typeof(Plan), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetPlanByRunId")]
        public async Task<IActionResult> GetByRun([FromRoute] Guid runId)
        {
            var result = await this._mediator.Send(new GetByRun.Query { RunId = runId });
            return Ok(result);
        }
    }
}

