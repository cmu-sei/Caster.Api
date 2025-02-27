// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using System.Runtime.Serialization;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Hosts
{
    public class GetAll
    {
        [DataContract(Name = "GetHostsQuery")]
        public class Query : IRequest<Host[]>
        {
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, Host[]>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ViewHosts], cancellationToken);

            public override async Task<Host[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                return await dbContext.Hosts
                    .ProjectTo<Host>(mapper.ConfigurationProvider)
                    .ToArrayAsync(cancellationToken);
            }
        }
    }
}

