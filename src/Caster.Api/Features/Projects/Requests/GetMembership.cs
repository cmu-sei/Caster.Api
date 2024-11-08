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
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using System.Linq;
using System.Text.Json.Serialization;

namespace Caster.Api.Features.Projects
{
    public class GetMembership
    {
        [DataContract(Name = "GetProjectMembershipQuery")]
        public record Query : IRequest<ProjectMembership>
        {
            /// <summary>
            /// Id of the ProjectMembership
            /// </summary>
            [JsonIgnore]
            public Guid Id { get; set; }
        }

        public class Handler(CasterContext _db, IMapper _mapper) : IRequestHandler<Query, ProjectMembership>
        {
            public async Task<ProjectMembership> Handle(Query request, CancellationToken cancellationToken)
            {
                var projectMembership = await _db.ProjectMemberships
                    .Where(x => x.Id == request.Id)
                    .ProjectTo<ProjectMembership>(_mapper.ConfigurationProvider)
                    .FirstOrDefaultAsync();

                if (projectMembership == null)
                    throw new EntityNotFoundException<ProjectMembership>();

                return projectMembership;
            }
        }
    }
}