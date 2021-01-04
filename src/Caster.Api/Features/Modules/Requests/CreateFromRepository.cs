// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using MediatR;
using Caster.Api.Data;
using System.Runtime.Serialization;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Identity;

namespace Caster.Api.Features.Modules
{
    public class CreateFromRepository
    {
        [DataContract(Name="CreateModuleRepositoryCommand")]
        public class Command : IRequest<bool>
        {
            /// <summary>
            /// Repository ID of the Module.
            /// </summary>
            [DataMember]
            public string Id { get; set; }
        }

        public class Handler : IRequestHandler<Command, bool>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;
            private readonly IGitlabRepositoryService _gitlabRepositoryService;

            public Handler(CasterContext db, IMapper mapper,
                IGitlabRepositoryService gitlabRepositoryService,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver)
            {
                _db = db;
                _mapper = mapper;
                _gitlabRepositoryService = gitlabRepositoryService;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
            }

            public async Task<bool> Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                // TODO: add handling for other repositories?
                var isSuccess = await _gitlabRepositoryService.GetModuleAsync(request.Id, cancellationToken);

                return isSuccess;
            }

        }
    }
}

