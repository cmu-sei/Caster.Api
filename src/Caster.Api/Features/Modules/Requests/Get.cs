// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Authorization;
using System.Linq;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.ModuleScopes;

namespace Caster.Api.Features.Modules
{
    public class Get
    {
        [DataContract(Name = "GetModuleQuery")]
        public class Query : IRequest<Module>
        {
            /// <summary>
            /// The Id of the Module to retrieve
            /// </summary>
            [DataMember]
            public Guid Id { get; set; }

            /// <summary>
            /// The project the Module is looked up for: it must be one the project may use, or one
            /// its Designs already use. Without it, the same is checked across the caller's own projects.
            /// </summary>
            [DataMember]
            public Guid? ProjectId { get; set; }
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            CasterContext dbContext,
            IModuleAccessService moduleAccess) : BaseHandler<Query, Module>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken)
            {
                if (request.ProjectId.HasValue)
                {
                    return await authorizationService.Authorize<Domain.Models.Project>(
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

            public override async Task<Module> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                if (!await moduleAccess.CanSeeModule(request.Id, request.ProjectId, cancellationToken))
                    throw new EntityNotFoundException<Module>();

                var module = await dbContext.Modules
                    .Include(m => m.Versions)
                    .SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);

                if (module == null)
                    throw new EntityNotFoundException<Module>();

                module.Versions = ModuleVersionOrder.HighestFirst(module.Versions);

                return module.ToModule();
            }
        }
    }
}
