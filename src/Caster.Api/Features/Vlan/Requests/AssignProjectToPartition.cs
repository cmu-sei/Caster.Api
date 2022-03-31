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
using Microsoft.EntityFrameworkCore;


namespace Caster.Api.Features.Vlan
{
    public class AssignProjectToPartition
    {
        [DataContract(Name="AssignProjectToPartitionCommand")]
        public class Command : IRequest<Partition>
        {
            /// <summary>
            /// The Id of the partition
            /// </summary>
            [DataMember]
            public Guid PartitionId { get; set; }
            
            /// <summary>
            /// The Id of project that will be assigned to this partition
            /// </summary>
            [DataMember]
            public Guid ProjectId { get; set; }
        }

        public class Handler : IRequestHandler<Command, Partition>
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

            public async Task<Partition> Handle(Command assignCommand, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                // Assign partition to project
                var project = await _db.Projects.SingleOrDefaultAsync(P => P.Id == assignCommand.ProjectId);
                if (project != null) {
                    project.PartitionId = assignCommand.PartitionId;
                    await _db.SaveChangesAsync();
                } else {
                    throw new EntityNotFoundException<string>(
                        String.Format(
                            "Unable to find the Project with ProjectId = {0}",
                            assignCommand.ProjectId
                        )
                    );
                }

                return _mapper.Map<Partition>(_db.Partitions.SingleOrDefault(P => P.Id == assignCommand.PartitionId));
            }
        }
    }
}
