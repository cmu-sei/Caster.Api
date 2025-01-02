// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Options;
using System.Linq;
using Caster.Api.Features.Shared;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Terraform
{
    public class GetMaxParallelism
    {
        [DataContract(Name = "GetTerraformMaxParallelism")]
        public class Query : IRequest<int>
        {
        }

        public class Handler(
            ICasterAuthorizationService authorizationService, TerraformOptions terraformOptions) : BaseHandler<Query, int>
        {
            public async override Task<bool> Authorize(Query request, CancellationToken cancellationToken)
            {
                if (authorizationService.GetAuthorizedProjectIds().Any())
                {
                    return true;
                }
                else
                {
                    return await authorizationService.Authorize([SystemPermission.ViewProjects], cancellationToken);
                }
            }

            public override Task<int> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                return Task.FromResult(terraformOptions.MaxParallelism);
            }
        }
    }
}
