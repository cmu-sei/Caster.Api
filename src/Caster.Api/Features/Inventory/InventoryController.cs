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
        /// Get the configured infrastructure connections. Never returns their urls
        /// or credentials.
        /// </summary>
        [HttpGet("connections")]
        [ProducesResponseType(typeof(InventoryConnection[]), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetInventoryConnections")]
        public async Task<IActionResult> GetConnections()
        {
            var result = await _mediator.Send(new GetConnections.Query());
            return Ok(result);
        }

        /// <summary>
        /// Get the cached VM templates available on an
        /// infrastructure connection, the default unless connection names another.
        /// </summary>
        [HttpGet("vm-templates")]
        [ProducesResponseType(typeof(InventoryResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetInventoryVmTemplates")]
        public async Task<IActionResult> GetVmTemplates([FromQuery] string connection = null)
        {
            var result = await _mediator.Send(new GetVmTemplates.Query { Connection = connection });
            return Ok(result);
        }

        /// <summary>
        /// Get the cached ISO images available on an
        /// infrastructure connection, the default unless connection names another.
        /// </summary>
        [HttpGet("isos")]
        [ProducesResponseType(typeof(InventoryResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetInventoryIsos")]
        public async Task<IActionResult> GetIsos([FromQuery] string connection = null)
        {
            var result = await _mediator.Send(new GetIsos.Query { Connection = connection });
            return Ok(result);
        }

        /// <summary>
        /// Get the cached networks available on an
        /// infrastructure connection, the default unless connection names another.
        /// </summary>
        [HttpGet("networks")]
        [ProducesResponseType(typeof(InventoryResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetInventoryNetworks")]
        public async Task<IActionResult> GetNetworks([FromQuery] string connection = null)
        {
            var result = await _mediator.Send(new GetNetworks.Query { Connection = connection });
            return Ok(result);
        }

        /// <summary>
        /// Get the cached datastores available on an
        /// infrastructure connection, the default unless connection names another.
        /// </summary>
        [HttpGet("datastores")]
        [ProducesResponseType(typeof(InventoryResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "GetInventoryDatastores")]
        public async Task<IActionResult> GetDatastores([FromQuery] string connection = null)
        {
            var result = await _mediator.Send(new GetDatastores.Query { Connection = connection });
            return Ok(result);
        }

        /// <summary>
        /// Trigger an immediate refresh of one connection's cached inventory, the
        /// default unless connection names another. Waits for the
        /// refresh to land, up to a fixed bound, so the get endpoints return the
        /// new data straight afterwards. If the bound expires the refresh carries
        /// on in the background and the response reports Completed = false.
        /// </summary>
        [HttpPost("refresh")]
        [ProducesResponseType(typeof(InventoryRefreshResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "RefreshInventory")]
        public async Task<IActionResult> Refresh([FromQuery] string connection = null)
        {
            var result = await _mediator.Send(new Refresh.Command { Connection = connection });
            return Ok(result);
        }
    }
}
