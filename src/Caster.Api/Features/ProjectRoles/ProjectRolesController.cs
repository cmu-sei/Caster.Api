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

namespace Caster.Api.Features.ProjectRoles;

[Route("api/project-roles")]
[ApiController]
[Authorize]
public class ProjectRolesController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProjectRolesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get a single ProjectRole.
    /// </summary>
    /// <param name="id">ID of an ProjectRole.</param>
    /// <returns></returns>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ProjectRole), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetProjectRole")]
    public async Task<IActionResult> Get([FromRoute] Guid id)
    {
        var result = await _mediator.Send(new Get.Query { Id = id });
        return Ok(result);
    }

    /// <summary>
    /// Get all ProjectRoles.
    /// </summary>
    /// <returns></returns>
    [HttpGet()]
    [ProducesResponseType(typeof(IEnumerable<ProjectRole>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetAllProjectRoles")]
    public async Task<IActionResult> GetAll()
    {
        var result = await _mediator.Send(new GetAll.Query());
        return Ok(result);
    }
}