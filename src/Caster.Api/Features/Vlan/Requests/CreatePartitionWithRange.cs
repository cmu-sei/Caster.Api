// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
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
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using System.Collections.Generic;


namespace Caster.Api.Features.Vlan
{
    public class CreatePartitionWithRange
    {
        [DataContract(Name="CreatePartitionWithRangeCommand")]
        public class Command : IRequest<Partition>
        {
            /// <summary>
            /// The Id of the Pool this partition is in
            /// </summary>
            [DataMember]
            public Guid PoolId { get; set; }
            
            /// <summary>
            /// The Id of project that will be assigned to this partition
            /// </summary>
            [DataMember]
            public Guid? ProjectId { get; set; }

            /// <summary>
            /// The Name of this partition is in
            /// </summary>
            [DataMember]
            public String Name { get; set; }

            /// <summary>
            /// The lowest vlan that the partition should include
            /// </summary>
            [DataMember]
            public int Lo { get; set; }

            /// <summary>
            /// The highest vlan that the partition should include
            /// </summary>
            [DataMember]
            public int Hi { get; set; }
        }

        public class Handler : IRequestHandler<Command, Partition>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;
            private readonly IIdentityResolver _identityResolver;

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
                _identityResolver = identityResolver;
            }

            public async Task<Partition> Handle(Command partitionCommand, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();
                
                // Input data validation
                if (partitionCommand.Hi - partitionCommand.Lo < 1 || 
                    partitionCommand.Hi <= 0 || partitionCommand.Hi > 4096 ||
                    partitionCommand.Lo < 0 || partitionCommand.Lo >= 4096) {
                    throw new Exception(
                        String.Format(
                            "The requested range must be between [0, 4096] and include >= 1 vlan, [{0}, {1}] is invalid",
                            partitionCommand.Lo,
                            partitionCommand.Hi
                        )
                    );
                }

                HashSet<int> requested = new HashSet<int>();
                for (int i = partitionCommand.Lo; i <= partitionCommand.Hi; i++) {
                    requested.Add(i);
                }
                
                // Find used vlans in this pool
                var usedVlans = _db.Vlans.Where(v => v.PoolId == partitionCommand.PoolId)
                    .ProjectTo<Vlan>(_mapper.ConfigurationProvider)
                    .ToHashSet<Vlan>();

                // Check that all requested vlans are available
                foreach (Vlan V in usedVlans) {
                    if (requested.Contains(V.vlan)) {
                        throw new ConflictException(
                            String.Format(
                                "The requested range of vlans [{0}, {1}] is unavailable",
                                partitionCommand.Lo,
                                partitionCommand.Hi
                            )
                        );
                    }
                }

                // Create partition
                var partition = _mapper.Map<Domain.Models.Partition>(partitionCommand);
                await _db.Partitions.AddAsync(partition);
                await _db.SaveChangesAsync();
                
                var finalPartition = _mapper.Map<Partition>(partition);

                // Update project with partitionId if ProjectId was provided
                var project = await _db.Projects.SingleOrDefaultAsync(P => P.Id == partitionCommand.ProjectId);
                if (project != null) {
                    project.PartitionId = finalPartition.Id;
                    await _db.SaveChangesAsync();
                }

                foreach (int vlan in requested) {
                    var vlanRequest = await new CreateVlan.Handler(
                        _db,
                        _mapper,
                        _authorizationService,
                        _identityResolver
                    ).Handle(
                        new CreateVlan.Command() { 
                            PoolId = partitionCommand.PoolId, 
                            PartitionId = finalPartition.Id,
                            Vlan = vlan
                        },
                        cancellationToken
                    );
                }

                return _mapper.Map<Partition>(_db.Partitions.Single(P => P.Id == finalPartition.Id));
            }
        }
    }
}
