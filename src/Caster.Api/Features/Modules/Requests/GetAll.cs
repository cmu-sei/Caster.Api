// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Domain.Services;
using Caster.Api.Domain.Services.Modules;
using Caster.Api.Domain.Services.ModuleScopes;
using System;
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
            /// only return the Modules this project may use: its Module Scopes and its project
            /// group's. Without it, a holder of ViewModules gets every Module, and anyone else the
            /// Modules their own projects together may use. Ignored with a DesignId, which lists
            /// the Modules the Design already uses.
            /// </summary>
            [DataMember]
            public Guid? ProjectId { get; set; }
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            CasterContext dbContext,
            IModuleRepositoryService moduleRepositoryService,
            IModuleAccessService moduleAccess,
            ILogger<GetAll> logger) : BaseHandler<Query, Module[]>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken)
            {
                // Naming a project asks for that project's Modules, so seeing the project is what is needed;
                // the catalog gate below is for the listing without one.
                if (request.ProjectId.HasValue)
                {
                    if (!await authorizationService.Authorize<Domain.Models.Project>(
                        request.ProjectId, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken))
                    {
                        return false;
                    }
                }
                else
                {
                    var canViewCatalog = authorizationService.GetAuthorizedProjectIds().Any()
                        || await authorizationService.Authorize([SystemPermission.ViewModules], cancellationToken);

                    if (!canViewCatalog)
                        return false;
                }

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
                else
                {
                    // A Design's own Modules above stay whatever the scopes say, so a Design
                    // that uses one outside them keeps working. Only what is offered is limited.
                    query = (await moduleAccess.ForCaller(request.ProjectId, cancellationToken)).Apply(query);

                    if (request.IncludeVersions)
                    {
                        query = query.Include(x => x.Versions);
                    }
                }

                // The versions are never projected: the Include calls above have no effect on a projection.
                var modules = await (request.IncludeVersionCount ? query.ProjectToModuleWithVersionCount() : query.ProjectToModule())
                    .ToArrayAsync(cancellationToken);

                foreach (var module in modules.Where(m => m.Versions != null))
                {
                    module.Versions = Domain.Models.ModuleVersionOrder.HighestFirst(module.Versions, v => v.Name, v => v.DateCreated);
                }

                return modules;
            }
        }
    }
}

