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

        public class Handler : IRequestHandler<Query, TerraformVersionsResult>
        {
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;
            private readonly ITerraformService _terraformService;
            private readonly TerraformOptions _terraformOptions;

            public Handler(
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver,
                ITerraformService terraformService,
                TerraformOptions terraformOptions)
            {
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
                _terraformService = terraformService;
                _terraformOptions = terraformOptions;
            }

            public async Task<TerraformVersionsResult> Handle(Query request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var versions = _terraformService.GetVersions();

                return new TerraformVersionsResult
                {
                    Versions = versions.ToArray(),
                    DefaultVersion = _terraformOptions.DefaultVersion
                };
            }
        }
    }
}
