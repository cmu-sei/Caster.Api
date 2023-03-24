// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Caster.Api.Features.Resources;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Caster.Api.Features.Proxmox;

[Route("api/")]
[ApiController]
[Authorize]
public class ProxmoxController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProxmoxController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Save a Proxmox Vm as a template
    /// </summary>
    [HttpPost("workspaces/{workspaceId}/resources/proxmox/vm/actions/save")]
    [ProducesResponseType(typeof(ResourceCommandResult), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "ProxmoxSaveVm")]
    public async Task<IActionResult> Get([FromRoute] Guid workspaceId, [FromBody] SaveVm.Command command)
    {
        command.WorkspaceId = workspaceId;
        var result = await _mediator.Send(command);
        return Ok(result);
    }


}
