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
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Identity;
using System;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using System.Linq;

namespace Caster.Api.Features.Groups
{
    public class GetMemberships
    {
        [DataContract(Name = "GetGroupMembershipsQuery")]
        public record Query : IRequest<GroupMembership[]>
        {
            public Guid GroupId { get; set; }
        }

        public class Validator : AbstractValidator<Query>
        {
            public Validator(IValidationService validationService)
            {
                RuleFor(x => x.GroupId).GroupExists(validationService);
            }
        }

        public class Handler(CasterContext _db, IMapper _mapper) : IRequestHandler<Query, GroupMembership[]>
        {
            public async Task<GroupMembership[]> Handle(Query request, CancellationToken cancellationToken)
            {
                return await _db.GroupMemberships
                    .Where(x => x.GroupId == request.GroupId)
                    .ProjectTo<GroupMembership>(_mapper.ConfigurationProvider)
                    .ToArrayAsync();
            }
        }
    }
}
