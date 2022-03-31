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
using System.Text.Json.Serialization;
using Caster.Api.Features.Projects;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;

namespace Caster.Api.Features.Vlan
{
    public class AssignPartition
    {
        [DataContract(Name = "AssignPartitionCommand")]
        public class Command : IRequest<Project>
        {
            /// <summary>
            /// The Id of the Partition
            /// </summary>
            [JsonIgnore]
            public Guid PartitionId { get; set; }

            /// <summary>
            /// The Id of the Project 
            /// </summary>
            [DataMember]
            public Guid ProjectId { get; set; }
        }

        public class Validator : AbstractValidator<Command>
        {
            public Validator(IValidationService validationService)
            {
                RuleFor(x => x.PartitionId).PartitionExists(validationService);
                RuleFor(x => x.ProjectId).ProjectExists(validationService);
            }
        }

        public class Handler : IRequestHandler<Command, Project>
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

            public async Task<Project> Handle(Command command, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var project = await _db.Projects
                    .Where(x => x.Id == command.ProjectId)
                    .FirstOrDefaultAsync(cancellationToken);

                project.PartitionId = command.PartitionId;

                await _db.SaveChangesAsync();

                return _mapper.Map<Project>(project);
            }
        }
    }
}
