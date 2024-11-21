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

namespace Caster.Api.Features.SystemRoles
{
    public class Create
    {
        [DataContract(Name = "CreateSystemRoleCommand")]
        public class Command : IRequest<SystemRole>
        {
            [DataMember]
            public Guid Id { get; set; }

            [DataMember]
            public string Name { get; set; }

            [DataMember]

            public bool AllPermissions { get; set; }

            [DataMember]
            public SystemPermission[] Permissions { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, SystemRole>
        {
            public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ManageRoles], cancellationToken);

            public override async Task<SystemRole> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var systemRole = mapper.Map<Domain.Models.SystemRole>(request);
                dbContext.SystemRoles.Add(systemRole);
                await dbContext.SaveChangesAsync();
                return mapper.Map<SystemRole>(systemRole);
            }
        }
    }
}

