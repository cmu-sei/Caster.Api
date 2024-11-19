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
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.SystemRoles
{
    public class Edit
    {
        [DataContract(Name = "EditSystemRoleCommand")]
        public class Command : IRequest<SystemRole>
        {
            [DataMember]
            public Guid Id { get; set; }

            [DataMember]
            public string Name { get; set; }

            [DataMember]

            public bool AllPermissions { get; set; }

            [DataMember]
            public SystemPermissions[] Permissions { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, SystemRole>
        {
            public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermissions.EditRoles], cancellationToken);

            public override async Task<SystemRole> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var systemRole = await dbContext.SystemRoles.FindAsync([request.Id], cancellationToken);

                if (systemRole == null)
                    throw new EntityNotFoundException<SystemRole>();

                mapper.Map(request, systemRole);
                await dbContext.SaveChangesAsync();
                return mapper.Map<SystemRole>(systemRole);
            }
        }
    }
}

