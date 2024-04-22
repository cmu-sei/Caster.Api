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

namespace Caster.Api.Features.Terraform
{
    [Route("api/")]
    [ApiController]
    [Authorize]
    public class TerraformController : ControllerBase
    {
        private readonly IMediator _mediator;

        public TerraformController(IMediator mediator)
        {
            this._mediator = mediator;
        }

        /// <summary>
        /// List all available Terraform versions.
        /// </summary>
        [HttpGet("terraform/versions")]
        [ProducesResponseType(typeof(GetVersions.TerraformVersionsResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetTerraformVersions")]
        public async Task<IActionResult> GetVersions()
        {
            var result = await this._mediator.Send(new GetVersions.Query());
            return Ok(result);
        }

        /// <summary>
        /// Get the maximum allowed parallelism setting value
        /// </summary>
        [HttpGet("terraform/max-parallelism")]
        [ProducesResponseType(typeof(int), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetTerraformMaxParallelism")]
        public async Task<IActionResult> GetMaxParallelism()
        {
            var result = await this._mediator.Send(new GetMaxParallelism.Query());
            return Ok(result);
        }
    }
}
