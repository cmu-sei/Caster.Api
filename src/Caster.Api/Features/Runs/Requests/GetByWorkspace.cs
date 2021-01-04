// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Domain.Models;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using System.Text.Json.Serialization;

namespace Caster.Api.Features.Runs
{
    public class GetByWorkspace
    {
        [DataContract(Name="GetRunsByWorkspaceQuery")]
        public class Query : RunQuery, IRequest<Run[]>
        {
            /// <summary>
            /// The Id of the Workspace whose Runs to retrieve
            /// </summary>
            [JsonIgnore]
            public Guid WorkspaceId { get; set; }

            /// <summary>
            /// Limit the number of results returned to this amount if present
            /// </summary>
            public int? Limit { get; set; }
        }

        public class Handler : IRequestHandler<Query, Run[]>
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

            public async Task<Run[]> Handle(Query request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                await ValidateWorkspace(request.WorkspaceId);

                return await _db.Runs
                    .Where(x => x.WorkspaceId == request.WorkspaceId)
                    .OrderByDescending(r => r.CreatedAt)
                    .Limit(request.Limit)
                    .Expand(_mapper.ConfigurationProvider,
                            includePlan: request.IncludePlan,
                            includeApply: request.IncludeApply)
                    .ToArrayAsync();
            }

            private async Task ValidateWorkspace(Guid workspaceId)
            {
                var workspace = await _db.Workspaces.FindAsync(workspaceId);

                if (workspace == null)
                    throw new EntityNotFoundException<Workspace>();
            }
        }
    }
}

