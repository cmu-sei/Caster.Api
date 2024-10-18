// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Caster.Api.Infrastructure.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Caster.Api.Features.ProjectPermissions;

[Route("api/permissions/project")]
[ApiController]
[Authorize]
public class ProjectPermissionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProjectPermissionsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get all SystemPermissions for the calling User.
    /// </summary>
    /// <returns></returns>
    [HttpGet("mine")]
    [ProducesResponseType(typeof(IEnumerable<ProjectPermissionsClaim>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetMyProjectPermissions")]
    public async Task<IActionResult> GetAll([FromQuery] GetMine.Query query)
    {
        var result = await _mediator.Send(query);
        return Ok(result);
    }
}