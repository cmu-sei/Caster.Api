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
using System.Collections.Generic;
using System.Linq;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.Extensions.Logging;

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

            /// <summary>
            /// Whether or not to retrieve the name of the highest version.
            /// </summary>
            [DataMember]
            public bool IncludeLatestVersion { get; set; }
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            IMapper mapper,
            CasterContext dbContext,
            IModuleRepositoryService moduleRepositoryService,
            ILogger<GetAll> logger) : BaseHandler<Query, Module[]>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken)
            {
                var canViewCatalog = authorizationService.GetAuthorizedProjectIds().Any()
                    || await authorizationService.Authorize([SystemPermission.ViewModules], cancellationToken);

                if (!canViewCatalog)
                    return false;

                return !request.DesignId.HasValue
                    || await authorizationService.Authorize<Domain.Models.Design>(
                        request.DesignId, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);
            }

            public override async Task<Module[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                // Sync every configured module source into the database first.
                // A failed sync still serves what is stored: every page that
                // lists Modules sends this, and one unreachable source should
                // not fail them all. Each source's failure is kept instead and
                // reported by GET api/modules/sources, so it stays
                // distinguishable from a source that has no Modules.
                try
                {
                    await moduleRepositoryService.GetModulesAsync(request.ForceUpdate, cancellationToken);
                }
                catch (Exception ex) when (ex is ModuleSyncException or ModuleSourceNotConfiguredException)
                {
                    logger.LogWarning("Serving the stored Modules: {Message}", ex.Message);
                }

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

                foreach (var module in modules.Where(m => m.Versions != null))
                {
                    module.Versions = Domain.Models.ModuleVersionOrder.HighestFirst(module.Versions, v => v.Name, v => v.DateCreated);
                }

                if (request.IncludeLatestVersion)
                {
                    await SetLatestVersions(modules, cancellationToken);
                }

                return modules;
            }

            /// <summary>
            /// Versions are ordered by parsing their names, which the database cannot do, so
            /// only the names and dates are read and the highest is picked here.
            /// </summary>
            private async Task SetLatestVersions(Module[] modules, CancellationToken cancellationToken)
            {
                var ids = modules.Select(m => m.Id).ToArray();

                var versions = await dbContext.ModuleVersions
                    .Where(v => ids.Contains(v.ModuleId))
                    .Select(v => new { v.ModuleId, v.Name, v.DateCreated })
                    .ToListAsync(cancellationToken);

                var latest = versions
                    .GroupBy(v => v.ModuleId)
                    .ToDictionary(
                        g => g.Key,
                        g => Domain.Models.ModuleVersionOrder.HighestFirst(g, v => v.Name, v => v.DateCreated)[0].Name);

                foreach (var module in modules)
                {
                    module.LatestVersion = latest.GetValueOrDefault(module.Id);
                }
            }
        }
    }
}

