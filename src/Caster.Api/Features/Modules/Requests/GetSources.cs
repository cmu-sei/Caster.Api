// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.Modules;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using MediatR;

namespace Caster.Api.Features.Modules
{
    public class GetSources
    {
        [DataContract(Name = "GetModuleSourcesQuery")]
        public class Query : IRequest<ModuleSource[]>
        {
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            IModuleRepositoryService moduleRepositoryService) : BaseHandler<Query, ModuleSource[]>
        {
            public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize([SystemPermission.ViewModules], cancellationToken);

            public override Task<ModuleSource[]> HandleRequest(Query request, CancellationToken cancellationToken) =>
                Task.FromResult(moduleRepositoryService.GetSourceStatuses()
                    .Select(x => new ModuleSource
                    {
                        Name = x.Source.Name,
                        Description = x.Source.Description,
                        Provider = x.Source.Provider,
                        LastAttempt = x.Status?.LastAttempt,
                        LastSuccess = x.Status?.LastSuccess,
                        Error = x.Status?.Error,
                    })
                    .ToArray());
        }
    }
}
