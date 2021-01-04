// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using System.Runtime.Serialization;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Identity;

namespace Caster.Api.Features.UserPermissions
{
    public class Delete
    {
        [DataContract(Name="DeleteUserPermissionCommand")]
        public class Command : IRequest
        {
            public Guid? Id { get; set; }
            public Guid? UserId { get; set; }
            public Guid? PermissionId { get; set; }
        }

        public class Handler : AsyncRequestHandler<Command>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;

            public Handler(
                CasterContext db,
                IMapper mapper,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver)
            {
                _db = db;
                _mapper = mapper;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
            }

            protected override async Task Handle(Command request, CancellationToken cancellationToken)
            {
                Caster.Api.Domain.Models.UserPermission entry;

                if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                    throw new ForbiddenException();

                if (request.Id != null)
                {
                    entry = _db.UserPermissions.FirstOrDefault(e => e.Id == request.Id);
                }
                else
                {
                    entry = _db.UserPermissions.FirstOrDefault(e => e.UserId == request.UserId && e.PermissionId == request.PermissionId);
                }

                if (entry == null)
                    throw new EntityNotFoundException<UserPermission>();

                _db.UserPermissions.Remove(entry);
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
    }
}

