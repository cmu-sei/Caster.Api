// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Caster.Api.Features.Assignments;

[Route("api")]
[ApiController]
[Authorize]
public class AssignmentsController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Add and remove Environment Sets and inventory connections on projects and project groups, all at once
    /// </summary>
    /// <remarks>
    /// The changes are checked against the final state and saved together, or not at all. Adding
    /// what is already assigned, or removing what isn't, changes nothing. Set changes need
    /// ManageEnvironmentSets, connection changes ManageInfrastructure.
    /// </remarks>
    [HttpPost("assignments")]
    [ProducesResponseType((int)HttpStatusCode.NoContent)]
    [SwaggerOperation(OperationId = "EditAssignments")]
    public async Task<IActionResult> Edit([FromBody] EditAssignments.Command command)
    {
        await mediator.Send(command);
        return NoContent();
    }
}
