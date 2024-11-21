// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Modules
{
    public class GetVersions
    {
        [DataContract(Name = "GetVersionsQuery")]
        public class Query : IRequest<ModuleVersion[]>
        {
            /// <summary>
            /// The Id of the Module to retrieve versions for
            /// </summary>
            [DataMember]
            public Guid ModuleId { get; set; }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Query, ModuleVersion[]>
        {
            public override async Task Authorize(Query request, CancellationToken cancellationToken)
            {
                if (!authorizationService.GetAuthorizedProjectIds().Any())
                    await authorizationService.Authorize([SystemPermission.ViewModules], cancellationToken);
            }

            public override async Task<ModuleVersion[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                return await dbContext.ModuleVersions
                    .Where(v => v.ModuleId == request.ModuleId)
                    .ProjectTo<ModuleVersion>(mapper.ConfigurationProvider)
                    .OrderByDescending(v => v.DateCreated)
                    .ToArrayAsync(cancellationToken);
            }
        }
    }
}
