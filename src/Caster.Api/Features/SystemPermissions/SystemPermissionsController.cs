// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Caster.Api.Features.SystemPermissions;

[Route("api/permissions")]
[ApiController]
[Authorize]
public class SystemPermissionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SystemPermissionsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get all SystemPermissions for the calling User.
    /// </summary>
    /// <returns></returns>
    [HttpGet("mine")]
    [ProducesResponseType(typeof(IEnumerable<Domain.Models.SystemPermission>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetMySystemPermissions")]
    public async Task<IActionResult> GetAll()
    {
        var result = await _mediator.Send(new GetMine.Query());
        return Ok(result);
    }
}