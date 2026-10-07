// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using AutoMapper.QueryableExtensions;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Domain.Services;
using Caster.Api.Domain.Services.Modules;
using System;
using System.Linq;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Modules
{
    public class GetAll
    {
        [DataContract(Name = "GetModulesQuery")]
        public class Query : IRequest<Module[]>
        {
            /// <summary>
            /// Whether or not to retrieve the module versions.
            /// </summary>
            [DataMember]
            public bool IncludeVersions { get; set; }

            /// <summary>
            /// Whether or not to retrieve the number of versions.
            /// </summary>
            [DataMember]
            public bool IncludeVersionCount { get; set; }

            /// <summary>
            /// force module update by ignoring DateModified.
            /// </summary>
            [DataMember]
            public bool ForceUpdate { get; set; }

            /// <summary>
            /// only return Modules in use by the specified Design
            /// </summary>
            [DataMember]
            public Guid? DesignId { get; set; }
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            IMapper mapper,
            CasterContext dbContext,
            IModuleRepositoryService moduleRepositoryService) : BaseHandler<Query, Module[]>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken)
            {
                if (authorizationService.GetAuthorizedProjectIds().Any())
                {
                    return true;
                }
                else
                {
                    return await authorizationService.Authorize([SystemPermission.ViewModules], cancellationToken);
                }
            }

            public override async Task<Module[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                // Sync every configured module source into the database first.
                // Failures propagate on purpose: this used to swallow every
                // exception, which made an unconfigured or broken module source
                // indistinguishable from a repository that genuinely has no
                // Modules. See ModuleSourceNotConfiguredException (503) and
                // ModuleSyncException (502).
                await moduleRepositoryService.GetModulesAsync(request.ForceUpdate, cancellationToken);

                IQueryable<Domain.Models.Module> query = dbContext.Modules;

                if (request.DesignId.HasValue)
                {
                    var designQuery = dbContext.DesignModules
                        .Include(x => x.Module)
                        .Where(x => x.DesignId == request.DesignId);

                    if (request.IncludeVersions)
                    {
                        designQuery = designQuery
                            .Include(x => x.Module)
                            .ThenInclude(x => x.Versions);
                    }

                    query = designQuery.Select(x => x.Module);
                }
                else if (request.IncludeVersions)
                {
                    query = query.Include(x => x.Versions);
                }

                var modules = await query
                    .ProjectTo<Module>(mapper.ConfigurationProvider, request.IncludeVersionCount ? (dest => dest.VersionsCount) : null)
                    .ToArrayAsync(cancellationToken);

                return modules;
            }
        }
    }
}

