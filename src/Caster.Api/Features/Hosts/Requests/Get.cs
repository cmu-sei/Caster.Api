// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using System.Runtime.Serialization;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Hosts
{
    public class Get
    {
        [DataContract(Name = "GetHostQuery")]
        public class Query : IRequest<Host>
        {
            /// <summary>
            /// The Id of the Host to retrieve
            /// </summary>
            [DataMember]
            public Guid Id { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, Host>
        {
            public override async Task Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ViewHosts], cancellationToken);

            public override async Task<Host> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var host = await dbContext.Hosts
                    .ProjectTo<Host>(mapper.ConfigurationProvider)
                    .SingleOrDefaultAsync(e => e.Id == request.Id, cancellationToken);

                if (host == null)
                    throw new EntityNotFoundException<Host>();

                return host;
            }
        }
    }
}

