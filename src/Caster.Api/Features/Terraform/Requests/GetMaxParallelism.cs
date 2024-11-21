// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Domain.Services;
using System;
using Caster.Api.Data;
using AutoMapper;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Caster.Api.Infrastructure.Options;
using System.Linq;
using Caster.Api.Features.Shared;

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
            public override Task Authorize(Query request, CancellationToken cancellationToken)
            {
                if (!authorizationService.GetAuthorizedProjectIds().Any())
                    throw new ForbiddenException();

                return Task.CompletedTask;
            }

            public override Task<int> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                return Task.FromResult(terraformOptions.MaxParallelism);
            }
        }
    }
}
