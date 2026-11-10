// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.Groups
{
    public class Create
    {
        [DataContract(Name = "CreateGroupCommand")]
        public class Command : IRequest<Group>
        {
            /// <summary>
            /// Name of the group.
            /// </summary>
            [DataMember]
            public string Name { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Command, Group>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ManageGroups], cancellationToken);

            public override async Task<Group> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var group = request.ToEntity();
                dbContext.Groups.Add(group);

                await dbContext.SaveChangesAsync();
                return group.ToGroup();
            }
        }
    }
}
