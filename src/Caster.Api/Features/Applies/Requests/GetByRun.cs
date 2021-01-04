// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Identity;

namespace Caster.Api.Features.Applies
{
    public class GetByRun
    {
        [DataContract(Name="GetApplyByRunQuery")]
        public class Query : IRequest<Apply>
        {
            /// <summary>
            /// Id of the Run whose Apply is to be retrieved
            /// </summary>
            [DataMember]
            public Guid RunId { get; set; }
        }

        public class Handler : IRequestHandler<Query, Apply>
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

            public async Task<Apply> Handle(Query request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var run = await _db.Runs.FirstOrDefaultAsync(x => x.Id == request.RunId);

                if (run == null)
                    throw new EntityNotFoundException<Run>();

                return await _db.Applies
                    .ProjectTo<Apply>(_mapper.ConfigurationProvider)
                    .SingleOrDefaultAsync(x => x.RunId == request.RunId, cancellationToken);
            }
        }
    }
}

