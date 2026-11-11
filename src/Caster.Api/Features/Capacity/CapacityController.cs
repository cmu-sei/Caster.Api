// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Threading.Tasks;
using Caster.Api.Domain.Services.Capacity;
using Caster.Api.Features.Projects;
using Caster.Api.Features.Workspaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Caster.Api.Features.Capacity;

/// <summary>Deployment limits, and the size check a run gets before it is applied.</summary>
[Route("api/")]
[ApiController]
[Authorize]
public class CapacityController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Check a Run's plan against its Workspace's limits and the room on the hypervisors
    /// </summary>
    /// <remarks>
    /// What applying the run would add, what the workspace would have, the
    /// limits in force, and the room on each host and datastore, as they are
    /// now. Apply makes the same check.
    /// </remarks>
    /// <param name="runId">The Id of the Run</param>
    [HttpGet("runs/{runId}/capacity")]
    [ProducesResponseType(typeof(CapacityCheck), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetRunCapacity")]
    public async Task<IActionResult> GetRunCapacity([FromRoute] Guid runId) =>
        Ok(await mediator.Send(new GetRunCapacity.Query { RunId = runId }));

    /// <summary>
    /// Get the limits in force for a Workspace
    /// </summary>
    /// <param name="id">The Id of the Workspace</param>
    [HttpGet("workspaces/{id}/limits")]
    [ProducesResponseType(typeof(EffectiveLimits), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetWorkspaceLimits")]
    public async Task<IActionResult> GetWorkspaceLimits([FromRoute] Guid id) =>
        Ok(await mediator.Send(new GetWorkspaceLimits.Query { Id = id }));

    /// <summary>
    /// Set a Workspace's own limits. Needs the EditProjects system permission
    /// </summary>
    /// <param name="id">The Id of the Workspace</param>
    /// <param name="command">The limits. A null clears that limit.</param>
    [HttpPut("workspaces/{id}/limits")]
    [ProducesResponseType(typeof(Workspace), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "EditWorkspaceLimits")]
    public async Task<IActionResult> EditWorkspaceLimits([FromRoute] Guid id, EditWorkspaceLimits.Command command)
    {
        command.Id = id;
        return Ok(await mediator.Send(command));
    }

    /// <summary>
    /// Get the limits in force for a Project's Workspaces that set none of their own
    /// </summary>
    /// <param name="id">The Id of the Project</param>
    [HttpGet("projects/{id}/limits")]
    [ProducesResponseType(typeof(EffectiveLimits), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetProjectLimits")]
    public async Task<IActionResult> GetProjectLimits([FromRoute] Guid id) =>
        Ok(await mediator.Send(new GetProjectLimits.Query { Id = id }));

    /// <summary>
    /// Set a Project's limits, which every Workspace in it is held to. Needs the EditProjects system permission
    /// </summary>
    /// <param name="id">The Id of the Project</param>
    /// <param name="command">The limits. A null clears that limit.</param>
    [HttpPut("projects/{id}/limits")]
    [ProducesResponseType(typeof(Project), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "EditProjectLimits")]
    public async Task<IActionResult> EditProjectLimits([FromRoute] Guid id, EditProjectLimits.Command command)
    {
        command.Id = id;
        return Ok(await mediator.Send(command));
    }
}
