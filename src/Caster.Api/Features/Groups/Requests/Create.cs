// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Runtime.Serialization;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Domain.Models;
using Microsoft.Extensions.DependencyInjection;

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

        public class Handler : IRequestHandler<Command, Group>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly ICasterAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;

            public Handler(
                CasterContext db,
                IMapper mapper,
                ICasterAuthorizationService authorizationService,
                IIdentityResolver identityResolver)
            {
                _db = db;
                _mapper = mapper;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
            }

            public async Task<Group> Handle(Command request, CancellationToken cancellationToken)
            {
                await _authorizationService.Authorize(AuthorizationType.Write, [SystemPermissions.CreateGroups]);

                var group = _mapper.Map<Domain.Models.Group>(request);
                _db.Groups.Add(group);

                // Add the creator as a member with the appropriate role
                var groupMembership = new Domain.Models.GroupMembership();
                groupMembership.UserId = _user.GetId();
                groupMembership.Group = group;
                _db.GroupMemberships.Add(groupMembership);

                await _db.SaveChangesAsync();
                return _mapper.Map<Group>(group);
            }
        }
    }
}
