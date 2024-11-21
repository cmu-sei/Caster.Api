// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using System.Runtime.Serialization;
using AutoMapper;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Users
{
    public class Create
    {
        [DataContract(Name = "CreateUserCommand")]
        public class Command : IRequest<User>
        {
            [DataMember]
            public Guid Id { get; set; }

            [DataMember]
            public string Name { get; set; }

            [DataMember]

            public bool AllPermissions { get; set; }

            [DataMember]
            public string RoleId { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, User>
        {
            public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ManageUsers], cancellationToken);

            public override async Task<User> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var user = mapper.Map<Domain.Models.User>(request);
                dbContext.Users.Add(user);
                await dbContext.SaveChangesAsync(cancellationToken);
                return mapper.Map<User>(user);
            }
        }
    }
}

