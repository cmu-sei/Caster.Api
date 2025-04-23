// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Caster.Api.Data;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Features.Shared;
using FluentValidation;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Identity;

namespace Caster.Api.Features.Directories
{
    public class GetMine
    {
        [DataContract(Name = "GetMyDirectoriesQuery")]
        public class Query : IRequest<Directory[]>
        {
        }

        public class Validator : AbstractValidator<Query>
        {
            public Validator(IValidationService validationService)
            {
            }
        }

        public class Handler(
            IMapper mapper,
            CasterContext dbContext,
            IIdentityResolver identityResolver) : BaseHandler<Query, Directory[]>
        {
             public override Task<bool> Authorize(Query request, CancellationToken cancellationToken) => Task.FromResult(true);

            public override async Task<Directory[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var userId = identityResolver.GetId();
                var myProjectIds = await dbContext.ProjectMemberships
                    .Where(pm => pm.UserId == userId)
                    .Select(pm => pm.ProjectId)
                    .ToListAsync(cancellationToken);

                var myDirectories = await dbContext.Directories
                    .Where(d => myProjectIds
                    .Contains(d.ProjectId))
                    .ProjectTo<Directory>(mapper.ConfigurationProvider)
                    .ToArrayAsync(cancellationToken);

                return myDirectories;
            }
        }
    }
}
