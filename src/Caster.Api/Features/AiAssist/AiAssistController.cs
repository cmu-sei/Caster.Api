// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Caster.Api.Features.AiAssist
{
    [Route("api/")]
    [ApiController]
    [Authorize]
    public class AiAssistController : ControllerBase
    {
        private readonly IMediator _mediator;

        public AiAssistController(IMediator mediator)
        {
            _mediator = mediator;
        }

        /// <summary>
        /// Ask AI for changes to a directory's files and workspaces
        /// </summary>
        /// <remarks>
        /// The model sees the directory's files and its parents', its workspaces with their Terraform versions and own files,
        /// the latest run's plan or apply output when it failed, and the enabled Designs. Returns a reply and the proposed
        /// changes, each labelled; nothing is saved. Apply the ones the planner keeps with ApplyAiAssistantChanges.
        /// </remarks>
        /// <param name="directoryId">ID of the directory.</param>
        /// <param name="command"></param>
        /// <returns></returns>
        [HttpPost("directories/{directoryId}/ai-assist")]
        [ProducesResponseType(typeof(AiAssistResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "AskAiAssistant")]
        public async Task<IActionResult> Ask([FromRoute] Guid directoryId, [FromBody] Ask.Command command)
        {
            command.DirectoryId = directoryId;
            var result = await _mediator.Send(command);
            return Ok(result);
        }

        /// <summary>
        /// Apply the Ask AI changes the planner kept
        /// </summary>
        /// <remarks>
        /// Each change is checked again against the directory as it is now and made through the same file and workspace
        /// requests, with the same permission and lock rules, as a change made by hand. A file that has changed since the
        /// proposal is not replaced. Returns whether each change was applied, and why not.
        /// </remarks>
        /// <param name="directoryId">ID of the directory.</param>
        /// <param name="command"></param>
        /// <returns></returns>
        [HttpPost("directories/{directoryId}/ai-assist/apply")]
        [ProducesResponseType(typeof(AiAssistApplyResult), (int)HttpStatusCode.OK)]
        [SwaggerOperation(OperationId = "ApplyAiAssistantChanges")]
        public async Task<IActionResult> Apply([FromRoute] Guid directoryId, [FromBody] Apply.Command command)
        {
            command.DirectoryId = directoryId;
            var result = await _mediator.Send(command);
            return Ok(result);
        }
    }
}
