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
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.Users
{
    public class Get
    {
        [DataContract(Name = "GetUserQuery")]
        public class Query : IRequest<User>
        {
            /// <summary>
            /// The Id of the User to retrieve
            /// </summary>
            [DataMember]
            public Guid Id { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, User>
        {
            public override async Task Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermissions.ViewUsers], cancellationToken);

            public override async Task<User> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var user = await dbContext.Users
                    .ProjectTo<User>(mapper.ConfigurationProvider)
                    .SingleOrDefaultAsync(e => e.Id == request.Id, cancellationToken);

                if (user == null)
                    throw new EntityNotFoundException<User>();

                return user;
            }
        }
    }
}

