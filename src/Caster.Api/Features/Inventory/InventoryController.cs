// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Caster.Api.Features.Inventory
{
    [Route("api/inventory")]
    [ApiController]
    [Authorize]
    public class InventoryController : ControllerBase
    {
        private readonly IMediator _mediator;

        public InventoryController(IMediator mediator)
        {
            _mediator = mediator;
        }

        /// <summary>
        /// Get the cached VM templates available on the configured infrastructure provider.
        /// </summary>
        [HttpGet("vm-templates")]
        [ProducesResponseType(typeof(InventoryResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetInventoryVmTemplates")]
        public async Task<IActionResult> GetVmTemplates()
        {
            var result = await _mediator.Send(new GetVmTemplates.Query());
            return Ok(result);
        }

        /// <summary>
        /// Get the cached ISO images available on the configured infrastructure provider.
        /// </summary>
        [HttpGet("isos")]
        [ProducesResponseType(typeof(InventoryResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetInventoryIsos")]
        public async Task<IActionResult> GetIsos()
        {
            var result = await _mediator.Send(new GetIsos.Query());
            return Ok(result);
        }

        /// <summary>
        /// Get the cached networks available on the configured infrastructure provider.
        /// </summary>
        [HttpGet("networks")]
        [ProducesResponseType(typeof(InventoryResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetInventoryNetworks")]
        public async Task<IActionResult> GetNetworks()
        {
            var result = await _mediator.Send(new GetNetworks.Query());
            return Ok(result);
        }

        /// <summary>
        /// Get the cached datastores available on the configured infrastructure provider.
        /// </summary>
        [HttpGet("datastores")]
        [ProducesResponseType(typeof(InventoryResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetInventoryDatastores")]
        public async Task<IActionResult> GetDatastores()
        {
            var result = await _mediator.Send(new GetDatastores.Query());
            return Ok(result);
        }

        /// <summary>
        /// Trigger an immediate refresh of the cached inventory. Waits for the
        /// refresh to land, up to a fixed bound, so the get endpoints return the
        /// new data straight afterwards. If the bound expires the refresh carries
        /// on in the background and the response reports Completed = false.
        /// </summary>
        [HttpPost("refresh")]
        [ProducesResponseType(typeof(InventoryRefreshResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "RefreshInventory")]
        public async Task<IActionResult> Refresh()
        {
            var result = await _mediator.Send(new Refresh.Command());
            return Ok(result);
        }
    }
}
