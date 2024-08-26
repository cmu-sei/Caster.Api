// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Caster.Api.Features.Projects;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Caster.Api.Features.Vlan;

[Route("api")]
[ApiController]
[Authorize]
public class VlansController : ControllerBase
{
    private readonly IMediator _mediator;

    public VlansController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Create a Pool of VLANs
    /// </summary>
    [HttpPost("vlans/pools")]
    [ProducesResponseType(typeof(Pool), (int)HttpStatusCode.Created)]
    [SwaggerOperation(OperationId = "CreatePool")]
    public async Task<IActionResult> CreatePool([FromBody] CreatePool.Command command)
    {
        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetPoolById), new { id = result.Id }, result);
    }

    /// <summary>
    /// Edit a VLAN Pool by Id
    /// </summary>
    /// <returns></returns>
    [HttpPut("vlans/pools/{id}")]
    [ProducesResponseType(typeof(Pool), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "EditPool")]
    public async Task<IActionResult> EditPool([FromRoute] Guid id, EditPool.Command command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Partial edit a VLAN Pool by Id
    /// </summary>
    /// <returns></returns>
    [HttpPatch("vlans/pools/{id}")]
    [ProducesResponseType(typeof(Pool), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "PartialEditPool")]
    public async Task<IActionResult> PartialEditPool([FromRoute] Guid id, PartialEditPool.Command command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Get all VLAN Pools
    /// </summary>
    /// <returns></returns>
    [HttpGet("vlans/pools")]
    [ProducesResponseType(typeof(IEnumerable<Pool>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetPools")]
    public async Task<IActionResult> GetPools()
    {
        var result = await _mediator.Send(new GetPools.Query());
        return Ok(result);
    }

    /// <summary>
    /// Get VLAN Pool by Id
    /// </summary>
    /// <returns></returns>
    [HttpGet("vlans/pools/{id}")]
    [ProducesResponseType(typeof(IEnumerable<Pool>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetPoolById")]
    public async Task<IActionResult> GetPoolById([FromRoute] Guid id)
    {
        var result = await _mediator.Send(new GetPool.Query() { PoolId = id });
        return Ok(result);
    }

    /// <summary>
    /// Delete a VLAN Pool
    /// </summary>
    /// <param name="id">ID of a VLAN Pool</param>
    /// <param name="command"></param>
    /// <returns></returns>
    [HttpDelete("vlans/pools/{id}")]
    [ProducesResponseType((int)HttpStatusCode.NoContent)]
    [SwaggerOperation(OperationId = "DeletePool")]
    public async Task<IActionResult> DeletePool([FromRoute] Guid id, [FromBody] DeletePool.Command command)
    {
        command.Id = id;
        await _mediator.Send(command);
        return NoContent();
    }

    /// <summary>
    /// Create a Partition in a Pool
    /// </summary>
    /// <param name="id">The Id of the Pool to create this Partition in</param>
    /// <param name="command"></param>
    [HttpPost("vlans/pools/{id}/partitions")]
    [ProducesResponseType(typeof(Partition), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "CreatePartition")]
    public async Task<IActionResult> CreatePartition([FromRoute] Guid id, [FromBody] CreatePartition.Command command)
    {
        command.PoolId = id;
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Get VLAN Partitions by Pool
    /// </summary>
    /// <returns></returns>
    [HttpGet("vlans/pools/{poolId}/partitions")]
    [ProducesResponseType(typeof(IEnumerable<Partition>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetPartitionsByPool")]
    public async Task<IActionResult> GetPartitionsByPool([FromRoute] Guid poolId)
    {
        var result = await _mediator.Send(new GetPartitions.Query
        {
            PoolId = poolId
        });
        return Ok(result);
    }

    /// <summary>
    /// Get all VLAN Partitions
    /// </summary>
    /// <returns></returns>
    [HttpGet("vlans/partitions")]
    [ProducesResponseType(typeof(IEnumerable<Partition>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetPartitions")]
    public async Task<IActionResult> GetPartitions()
    {
        var result = await _mediator.Send(new GetPartitions.Query
        {
        });
        return Ok(result);
    }

    /// <summary>
    /// Get VLAN Partition by Id
    /// </summary>
    /// <returns></returns>
    [HttpGet("vlans/partitions/{id}")]
    [ProducesResponseType(typeof(Partition), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetPartition")]
    public async Task<IActionResult> GetPartition([FromRoute] Guid id)
    {
        var result = await _mediator.Send(new GetPartition.Query
        {
            Id = id
        });
        return Ok(result);
    }

    /// <summary>
    /// Edit a VLAN Partition by Id
    /// </summary>
    /// <returns></returns>
    [HttpPut("vlans/partitions/{id}")]
    [ProducesResponseType(typeof(Partition), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "EditPartition")]
    public async Task<IActionResult> EditPartition([FromRoute] Guid id, EditPartition.Command command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Partial edit a VLAN Partition by Id
    /// </summary>
    /// <returns></returns>
    [HttpPatch("vlans/partitions/{id}")]
    [ProducesResponseType(typeof(Partition), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "PartialEditPartition")]
    public async Task<IActionResult> PartialEditPartition([FromRoute] Guid id, PartialEditPartition.Command command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Set a Partition as the system default
    /// </summary>
    /// <returns></returns>
    [HttpPost("vlans/partitions/{id}/actions/set-default")]
    [ProducesResponseType((int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "SetDefaultPartition")]
    public async Task<IActionResult> SetDefaultPartition([FromRoute] Guid id)
    {
        await _mediator.Send(new SetDefaultPartition.Command { Id = id });
        return Ok();
    }

    /// <summary>
    /// Set a Partition as the system default
    /// </summary>
    /// <returns></returns>
    [HttpPost("vlans/partitions/actions/unset-default")]
    [ProducesResponseType((int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "UnsetDefaultPartition")]
    public async Task<IActionResult> UnsetDefaultPartition()
    {
        await _mediator.Send(new SetDefaultPartition.Command());
        return Ok();
    }

    /// <summary>
    /// Delete a VLAN Partition
    /// </summary>
    /// <param name="id">ID of a VLAN Partition</param>
    /// <returns></returns>
    [HttpDelete("vlans/partitions/{id}")]
    [ProducesResponseType((int)HttpStatusCode.NoContent)]
    [SwaggerOperation(OperationId = "DeletePartition")]
    public async Task<IActionResult> DeletePartition([FromRoute] Guid id)
    {
        await _mediator.Send(new DeletePartition.Command { Id = id });
        return NoContent();
    }

    /// <summary>
    /// Assign a Partition to a Project
    /// </summary>
    [HttpPost("vlans/partitions/{id}/actions/assign")]
    [ProducesResponseType(typeof(Project), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "AssignPartition")]
    public async Task<IActionResult> AssignPartition([FromRoute] Guid id, [FromBody] AssignPartition.Command command)
    {
        command.PartitionId = id;
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Unassign a Partition to a Project
    /// </summary>
    [HttpPost("vlans/partitions/actions/unassign")]
    [ProducesResponseType(typeof(Project), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "UnassignPartition")]
    public async Task<IActionResult> UnassignPartition([FromBody] UnassignPartition.Command command)
    {
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Add unassigned VLANs to a Partition from it's Pool
    /// </summary>
    /// <returns></returns>
    [HttpPost("vlans/partitions/{id}/actions/vlans/add")]
    [ProducesResponseType(typeof(Vlan[]), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "AddVlansToPartition")]
    public async Task<IActionResult> AddVlansToPartition([FromRoute] Guid id, AddVlansToPartition.Command command)
    {
        command.PartitionId = id;
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Set VLANs from this Partition back to unassigned
    /// </summary>
    /// <returns></returns>
    [HttpPost("vlans/partitions/{id}/actions/vlans/remove")]
    [ProducesResponseType(typeof(Vlan[]), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "RemoveVlansFromPartition")]
    public async Task<IActionResult> RemoveVlansFromPartition([FromRoute] Guid id, RemoveVlansFromPartition.Command command)
    {
        command.PartitionId = id;
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Acquire a vlan, marking it as unavailable to others
    /// </summary>
    /// <returns></returns>
    [HttpPost("vlans/actions/acquire")]
    [ProducesResponseType(typeof(Vlan), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "AcquireVlan")]
    public async Task<IActionResult> AcquireVlan(AcquireVlan.Command command)
    {
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Release a vlan back to a partition
    /// </summary>
    /// <returns></returns>
    [HttpPost("vlans/{id}/actions/release")]
    [ProducesResponseType(typeof(Vlan), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "ReleaseVlan")]
    public async Task<IActionResult> ReleaseVlan([FromRoute] Guid id)
    {
        var result = await _mediator.Send(new ReleaseVlan.Command() { Id = id });
        return Ok(result);
    }

    /// <summary>
    /// Get a VLAN by Id
    /// </summary>
    /// <returns></returns>
    [HttpGet("vlans/{id}")]
    [ProducesResponseType(typeof(Vlan), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetVlan")]
    public async Task<IActionResult> GetVlan([FromRoute] Guid id)
    {
        var result = await _mediator.Send(new GetVlan.Query() { Id = id });
        return Ok(result);
    }

    /// <summary>
    /// Edit a VLAN, ignoring fields not provided
    /// </summary>
    /// <returns></returns>
    [HttpPatch("vlans/{id}")]
    [ProducesResponseType(typeof(Vlan), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "PartialEditVlan")]
    public async Task<IActionResult> PartialEditVlan([FromRoute] Guid id, PartialEditVlan.Command command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Get VLANs by Pool
    /// </summary>
    /// <returns></returns>
    [HttpGet("vlans/pools/{id}/vlans")]
    [ProducesResponseType(typeof(IEnumerable<Vlan>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetVlansByPool")]
    public async Task<IActionResult> GetVlansByPool([FromRoute] Guid id)
    {
        var result = await _mediator.Send(new GetVlans.Query
        {
            PoolId = id
        });
        return Ok(result);
    }

    /// <summary>
    /// Get VLANs by Partition
    /// </summary>
    /// <returns></returns>
    [HttpGet("vlans/partitions/{id}/vlans")]
    [ProducesResponseType(typeof(IEnumerable<Vlan>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "GetVlansByPartition")]
    public async Task<IActionResult> GetVlansByPartition([FromRoute] Guid id)
    {
        var result = await _mediator.Send(new GetVlans.Query
        {
            PartitionId = id
        });
        return Ok(result);
    }

    /// <summary>
    /// Reassign VLANs to the selected Partition
    /// </summary>
    /// <returns></returns>
    [HttpPost("vlans/actions/reassign-vlans")]
    [ProducesResponseType(typeof(IEnumerable<Vlan>), (int)HttpStatusCode.OK)]
    [SwaggerOperation(OperationId = "ReassignVlans")]
    public async Task<IActionResult> ReassignVlans(ReassignVlans.Command command)
    {
        var result = await _mediator.Send(command);
        return Ok(result);
    }
}