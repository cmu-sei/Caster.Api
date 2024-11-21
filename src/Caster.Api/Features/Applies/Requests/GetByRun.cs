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
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.Applies
{
    public class GetByRun
    {
        [DataContract(Name = "GetApplyByRunQuery")]
        public class Query : IRequest<Apply>
        {
            /// <summary>
            /// Id of the Run whose Apply is to be retrieved
            /// </summary>
            [DataMember]
            public Guid RunId { get; set; }
        }

        public class Handler : BaseHandler<Query, Apply>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly ICasterAuthorizationService _authorizationService;

            public Handler(
                CasterContext db,
                IMapper mapper,
                ICasterAuthorizationService authorizationService)
            {
                _db = db;
                _mapper = mapper;
                _authorizationService = authorizationService;
            }

            public override async Task Authorize(Query request, CancellationToken cancellationToken) =>
               await _authorizationService.Authorize<Run>(request.RunId, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);

            public override async Task<Apply> HandleRequest(Query request, CancellationToken cancellationToken)
            {
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

