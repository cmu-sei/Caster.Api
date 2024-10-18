// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using AutoMapper;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Groups
{
    public class Edit
    {
        [DataContract(Name = "EditGroupCommand")]
        public class Command : IRequest<Group>
        {
            [DataMember]
            public Guid Id { get; set; }

            /// <summary>
            /// Name of the group.
            /// </summary>
            [DataMember]
            public string Name { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, Group>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ManageGroups], cancellationToken);

            public override async Task<Group> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var group = await dbContext.Groups.FindAsync([request.Id], cancellationToken);

                if (group == null)
                    throw new EntityNotFoundException<Group>();

                mapper.Map(request, group);
                await dbContext.SaveChangesAsync(cancellationToken);
                return mapper.Map<Group>(group);
            }
        }
    }
}
