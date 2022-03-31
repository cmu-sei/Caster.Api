// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Domain.Services;

namespace Caster.Api.Features.Vlan
{
    public class GetVlanByProject
    {
        [DataContract(Name="GetVlanByProjectQuery")]
        public class Query : IRequest<Vlan>
        {
            /// <summary>
            /// The Id of the project that is requesting a vlan
            /// </summary>
            [DataMember]
            public Guid ProjectId { get; set; }
        }

        public class Handler : IRequestHandler<Query, Vlan>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;
            private readonly IIdentityResolver _identityResolver;
            private readonly ILockService _lockService;

            public Handler(
                CasterContext db,
                IMapper mapper,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver,
                ILockService lockService)
            {
                _db = db;
                _mapper = mapper;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
                _identityResolver = identityResolver;
                _lockService = lockService;
            }

            public async Task<Vlan> Handle(Query vlanRequest, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                // Find partition associated with project
                var project = _db.Projects.SingleOrDefault(P => P.Id == vlanRequest.ProjectId);
                Guid? partitionId = null;
                if (project != null) {
                    partitionId = project.PartitionId;
                }

                if (partitionId == null) {
                    throw new Exception(
                        String.Format(
                            "The project {0} has no assigned vlan partition",
                            project.Name
                        )
                    );
                }

                var partitionLock = _lockService.GetPartitionLock(partitionId.GetValueOrDefault());

                lock (partitionLock) {
                    var vlans = _db.Vlans.Where(v => v.PartitionId == partitionId.GetValueOrDefault() && !v.InUse)
                        .ToArray();

                    if (vlans.Length > 0) {
                        var vlan = vlans[0];
                        vlan.InUse = true;
                        _db.SaveChanges(); // Probably make this SaveChangesAsync

                        return _mapper.Map<Vlan>(vlan);
                    } else {
                        throw new Exception(
                            String.Format(
                                "Partition ({0}) has no available vlans",
                                _mapper.Map<Partition>(_db.Partitions.SingleOrDefault(P => P.Id == partitionId.GetValueOrDefault())).Name
                            )
                        );
                    }
                }
            }
        }
    }
}
