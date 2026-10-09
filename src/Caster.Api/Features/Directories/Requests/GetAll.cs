// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Directories
{
    public class GetAll
    {
        [DataContract(Name = "GetDirectoriesQuery")]
        public class Query : IRequest<Directory[]>
        {
            /// <summary>
            /// Whether or not to return related objects (Files, Workspaces)
            /// </summary>
            [DataMember]
            public bool IncludeRelated { get; set; }

            /// <summary>
            /// Whether or not to include contents of returned Files. Ignored if IncludeRelated is false
            /// </summary>
            [DataMember]
            public bool IncludeFileContent { get; set; }
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            IMapper mapper,
            CasterContext dbContext) : BaseHandler<Query, Directory[]>
        {
            public override Task<bool> Authorize(Query request, CancellationToken cancellationToken) => Task.FromResult(true);

            public override async Task<Directory[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var query = dbContext.Directories.AsQueryable();

                if (!await authorizationService.Authorize([SystemPermission.ViewProjects], cancellationToken))
                {
                    var projectIds = authorizationService.GetProjectPermissions()
                        .Where(p => p.Permissions.Contains(ProjectPermission.ViewProject))
                        .Select(p => p.ProjectId)
                        .Distinct()
                        .ToArray();

                    query = query.Where(d => projectIds.Contains(d.ProjectId));
                }

                return await query
                    .Expand(mapper.ConfigurationProvider, request.IncludeRelated, request.IncludeFileContent)
                    .ToArrayAsync(cancellationToken);
            }
        }
    }
}
