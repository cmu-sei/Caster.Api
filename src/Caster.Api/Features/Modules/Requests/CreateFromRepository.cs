// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Domain.Services;
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
            /// Repository ID of the Module.
            /// </summary>
            [DataMember]
            public string Id { get; set; }
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            IGitlabRepositoryService gitlabRepositoryService) : BaseHandler<Command, bool>
        {
            public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermissions.EditModules], cancellationToken);

            public override async Task<bool> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                // TODO: add handling for other repositories?
                return await gitlabRepositoryService.GetModuleAsync(request.Id, cancellationToken);
            }
        }
    }
}

