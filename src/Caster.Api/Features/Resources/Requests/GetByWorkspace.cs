// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using System.Runtime.Serialization;
using Caster.Api.Data;
using System;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Exceptions;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using System.Text.Json.Serialization;

namespace Caster.Api.Features.Resources
{
    public class GetByWorkspace
    {
        [DataContract(Name="GetResourcesByWorkspaceQuery")]
        public class Query : IRequest<Resource[]>
        {
            /// <summary>
            /// ID of the Workspace.
            /// </summary>
            [JsonIgnore]
            public Guid WorkspaceId { get; set; }
        }

        public class Handler : IRequestHandler<Query, Resource[]>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;

            public Handler(
                CasterContext db,
                IMapper mapper,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver)
            {
                _db = db;
                _mapper = mapper;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
            }

            public async Task<Resource[]> Handle(Query request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var workspace = await _db.Workspaces.FindAsync(request.WorkspaceId);

                if (workspace == null)
                {
                    throw new EntityNotFoundException<Workspace>();
                }

                var state = workspace.GetState();
                return _mapper.Map<Resource[]>(state.GetResources(), opts => opts.ExcludeMembers(nameof(Resource.Attributes)));
            }
        }
    }
}

