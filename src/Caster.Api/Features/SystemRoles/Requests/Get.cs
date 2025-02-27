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
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.SystemRoles
{
    public class Get
    {
        [DataContract(Name = "GetSystemRoleQuery")]
        public class Query : IRequest<SystemRole>
        {
            /// <summary>
            /// The Id of the SystemRole to retrieve
            /// </summary>
            [DataMember]
            public Guid Id { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, SystemRole>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([Domain.Models.SystemPermission.ViewRoles], cancellationToken);

            public override async Task<SystemRole> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var systemRole = await dbContext.SystemRoles
                    .ProjectTo<SystemRole>(mapper.ConfigurationProvider, dest => dest.Permissions)
                    .SingleOrDefaultAsync(e => e.Id == request.Id, cancellationToken);

                if (systemRole == null)
                    throw new EntityNotFoundException<SystemRole>();

                return systemRole;
            }
        }
    }
}

