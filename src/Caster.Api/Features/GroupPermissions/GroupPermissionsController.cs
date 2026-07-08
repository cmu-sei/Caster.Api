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

namespace Caster.Api.Features.GroupPermissions;

[Route("api/permissions/group")]
[ApiController]
[Authorize]
public class GroupPermissionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public GroupPermissionsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get all GroupPermissions for the calling User.
    /// </summary>
    /// <returns></returns>
    [HttpGet("mine")]
    [ProducesResponseType(typeof(IEnumerable<GroupPermissionsClaim>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetMyGroupPermissions")]
    public async Task<IActionResult> GetAll([FromQuery] GetMine.Query query)
    {
        var result = await _mediator.Send(query);
        return Ok(result);
    }
}
