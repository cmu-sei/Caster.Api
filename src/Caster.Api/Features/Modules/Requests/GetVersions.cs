// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.ModuleScopes;
using Caster.Api.Infrastructure.Exceptions;

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

            /// <summary>
            /// The project the Module's versions are looked up for. See <see cref="Get.Query.ProjectId"/>.
            /// </summary>
            [DataMember]
            public Guid? ProjectId { get; set; }
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            CasterContext dbContext,
            IModuleAccessService moduleAccess) : BaseHandler<Query, ModuleVersion[]>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken)
            {
                if (request.ProjectId.HasValue)
                {
                    return await authorizationService.Authorize<Project>(
                        request.ProjectId, [SystemPermission.ViewProjects], [ProjectPermission.ViewProject], cancellationToken);
                }

                if (authorizationService.GetAuthorizedProjectIds().Any())
                {
                    return true;
                }
                else
                {
                    return await authorizationService.Authorize([SystemPermission.ViewModules], cancellationToken);
                }
            }

            public override async Task<ModuleVersion[]> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                if (!await moduleAccess.CanSeeModule(request.ModuleId, request.ProjectId, cancellationToken))
                    throw new EntityNotFoundException<Module>();

                var versions = await dbContext.ModuleVersions
                    .Where(v => v.ModuleId == request.ModuleId)
                    .ProjectToModuleVersion()
                    .ToArrayAsync(cancellationToken);

                return Domain.Models.ModuleVersionOrder.HighestFirst(versions, v => v.Name, v => v.DateCreated).ToArray();
            }
        }
    }
}
