// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Domain.Services;
using Caster.Api.Domain.Services.Modules;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Modules
{
    public class CreateFromRepository
    {
        [DataContract(Name = "CreateModuleRepositoryCommand")]
        public class Command : IRequest<bool>
        {
            /// <summary>
            /// Identifies the Module to sync. For a git module source this is
            /// the source Name, or Name/subdirectory for a source using the
            /// Subdirectories layout. For the legacy Gitlab source it is the
            /// Gitlab project id, unchanged.
            /// </summary>
            [DataMember]
            public string Id { get; set; }
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            IModuleRepositoryService moduleRepositoryService) : BaseHandler<Command, bool>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ManageModules], cancellationToken);

            public override async Task<bool> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                return await moduleRepositoryService.GetModuleAsync(request.Id, cancellationToken);
            }
        }
    }
}

