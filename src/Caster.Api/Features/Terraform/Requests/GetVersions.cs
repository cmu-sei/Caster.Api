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
    public class GetVersions
    {
        [DataContract(Name = "GetTerraformVersions")]
        public class Query : IRequest<TerraformVersionsResult>
        {
        }

        public class TerraformVersionsResult
        {
            public string[] Versions { get; set; }
            public string DefaultVersion { get; set; }
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            ITerraformService terraformService,
            TerraformOptions terraformOptions) : BaseHandler<Query, TerraformVersionsResult>
        {
            public override Task Authorize(Query request, CancellationToken cancellationToken)
            {
                if (!authorizationService.GetAuthorizedProjectIds().Any())
                    throw new ForbiddenException();

                return Task.CompletedTask;
            }

            public override Task<TerraformVersionsResult> HandleRequest(Query request, CancellationToken cancellationToken)
            {
                var versions = terraformService.GetVersions();

                return Task.FromResult(new TerraformVersionsResult
                {
                    Versions = versions.ToArray(),
                    DefaultVersion = terraformOptions.DefaultVersion
                });
            }
        }
    }
}
