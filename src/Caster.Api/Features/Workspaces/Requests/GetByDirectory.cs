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

namespace Caster.Api.Features.Workspaces
{
    public class GetByDirectory
    {
        [DataContract(Name="GetWorkspacesByDirectoryQuery")]
        public class Query : IRequest<Workspace[]>
        {
            /// <summary>
            /// The Id of the Directory whose Workspaces to retrieve
            /// </summary>
            [DataMember]
            public Guid DirectoryId { get; set; }
        }

        public class Handler : IRequestHandler<Query, Workspace[]>
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

            public async Task<Workspace[]> Handle(Query request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                await ValidateEntities(request.DirectoryId);

                return await _db.Workspaces
                    .Where(x => x.DirectoryId == request.DirectoryId)
                    .ProjectTo<Workspace>(_mapper.ConfigurationProvider)
                    .ToArrayAsync();
            }

            private async Task ValidateEntities(Guid directoryId)
            {
                var directory = await _db.Directories.FindAsync(directoryId);

                if (directory == null)
                    throw new EntityNotFoundException<Directory>();
            }
        }
    }
}

