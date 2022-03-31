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

namespace Caster.Api.Features.Vlan
{
    [Route("api/vlan")]
    [ApiController]
    [Authorize]
    public class VlanController : ControllerBase
    {
        private readonly IMediator _mediator;

        public VlanController(IMediator mediator) 
        {
            _mediator = mediator;
        }

        /// <summary>
        /// Create a pool for a project.
        /// </summary>
        /// <param name="name">Name of the pool.</param>
        /// <returns></returns>
        [HttpPost("/pools/create/{name}")]
        [ProducesResponseType(typeof(IEnumerable<Pool>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "CreatePool")]
        public async Task<IActionResult> CreatePool([FromRoute] String name)
        {
            var result = await _mediator.Send(new CreatePool.Command() { Name = name });
            return Ok(result);
        }

        /// <summary>
        /// Get all pools
        /// </summary>
        /// <returns></returns>
        [HttpGet("/pools/get/all")]
        [ProducesResponseType(typeof(IEnumerable<Pool>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetPools")]
        public async Task<IActionResult> GetPools()
        {
            var result = await _mediator.Send(new GetPools.Query());
            return Ok(result);
        }

        /// <summary>
        /// Get pool by Id
        /// </summary>
        /// <returns></returns>
        [HttpGet("/pools/get/id/{poolId}")]
        [ProducesResponseType(typeof(IEnumerable<Pool>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetPoolById")]
        public async Task<IActionResult> GetPoolById([FromRoute] Guid poolId)
        {
            var result = await _mediator.Send(new GetPoolById.Query() { PoolId = poolId });
            return Ok(result);
        }

        /// <summary>
        /// Create a partition in a pool
        /// </summary>
        /// <param name="poolId">The Id of a pool</param>
        /// <param name="projectId">The Id of the project tied to this partition</param>
        /// <param name="name">The name of the new partition</param>
        /// <param name="requestedVlans">The number of requested Vlans</param>
        [HttpPost("/partitions/create/{name}")]
        [ProducesResponseType(typeof(IEnumerable<Partition>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "CreatePartition")]
        public async Task<IActionResult> CreatePartition([FromQuery] Guid poolId, [FromQuery] Guid projectId, [FromRoute] String name, [FromQuery] int requestedVlans)
        {
           var result = await _mediator.Send(new CreatePartition.Command() { 
                PoolId = poolId, 
                ProjectId = projectId,
                Name = name,
                RequestedVlans = requestedVlans 
            });
            return Ok(result);
        }

        /// <summary>
        /// Create a partition of vlans in a pool ranging from lo to hi (inclusive)
        /// </summary>
        /// <param name="poolId">The Id of a pool</param>
        /// <param name="projectId">The Id of the project tied to this partition</param>
        /// <param name="name">The name of the new partition</param>
        /// <param name="lo">The lowest vlan that the partition should include</param>
        /// <param name="hi">The highest vlan that the partition should include</param>
        [HttpPost("/partitions/createwithrange/{name}")]
        [ProducesResponseType(typeof(IEnumerable<Partition>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "CreatePartitionWithRange")]
        public async Task<IActionResult> CreatePartitionWithRange([FromQuery] Guid poolId, [FromQuery] Guid projectId, [FromRoute] String name, [FromQuery] int lo, [FromQuery] int hi)
        {
            var result = await _mediator.Send(new CreatePartitionWithRange.Command() { 
                PoolId = poolId, 
                ProjectId = projectId,
                Name = name,
                Lo = lo,
                Hi = hi
            });
            return Ok(result);
        }

        /// <summary>
        /// Get a vlan from a partition
        /// </summary>
        /// <returns></returns>
        [HttpPost("/vlan/get/{partitionId}")]
        [ProducesResponseType(typeof(IEnumerable<Vlan>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetVlan")]
        public async Task<IActionResult> GetVlan([FromRoute] Guid partitionId)
        {
            var result = await _mediator.Send(new GetVlan.Query() { PartitionId = partitionId });
            return Ok(result);
        }

        /// <summary>
        /// Return a vlan back to a partition by vlanId
        /// </summary>
        /// <returns></returns>
        [HttpPut("/vlan/return/id/{vlanId}")]
        [ProducesResponseType(typeof(IEnumerable<Vlan>), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "ReturnVlanById")]
        public async Task<IActionResult> ReturnVlanById([FromRoute] Guid vlanId)
        {
            var result = await _mediator.Send(new ReturnVlanById.Command() { Id = vlanId });
            return Ok(result);
        }        
    }
}

