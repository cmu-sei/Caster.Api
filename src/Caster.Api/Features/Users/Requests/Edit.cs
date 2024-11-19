// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using AutoMapper;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.Users
{
    public class Edit
    {
        [DataContract(Name = "EditUserCommand")]
        public class Command : IRequest<User>
        {
            [DataMember]
            public Guid Id { get; set; }

            [DataMember]
            public string Name { get; set; }

            [DataMember]
            public string RoleId { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, User>
        {
            public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermissions.EditUsers], cancellationToken);

            public override async Task<User> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var user = await dbContext.Users.FindAsync([request.Id], cancellationToken);

                if (user == null)
                    throw new EntityNotFoundException<User>();

                mapper.Map(request, user);
                await dbContext.SaveChangesAsync(cancellationToken);
                return mapper.Map<User>(user);
            }
        }
    }
}

