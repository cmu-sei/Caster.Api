// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using System.Runtime.Serialization;
using Caster.Api.Data;
using System;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Domain.Services;
using Microsoft.Extensions.Logging;
using Caster.Api.Infrastructure.Identity;
using System.Text.Json.Serialization;

namespace Caster.Api.Features.Resources
{
    public class Import
    {
        [DataContract(Name = "ImportResourceCommand")]
        public class Command : IRequest<ResourceCommandResult>
        {
            /// <summary>
            /// ID of the Workspace.
            /// </summary>
            [JsonIgnore]
            public Guid WorkspaceId { get; set; }

            /// <summary>
            /// Address of the Resource to import
            /// </summary>
            [DataMember]
            public string ResourceAddress { get; set; }

            /// <summary>
            /// Id of the Resource to import
            /// </summary>
            [DataMember]
            public string ResourceId { get; set; }
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            IMapper mapper,
            CasterContext dbContext,
            ILockService lockService,
            TerraformOptions terraformOptions,
            ITerraformService terraformService) : BaseOperationHandler<Command, ResourceCommandResult>(mapper, dbContext, lockService, terraformOptions, terraformService)
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Workspace>(request.WorkspaceId, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

            public override async Task<ResourceCommandResult> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var workspace = await base.GetWorkspace(request.WorkspaceId);

                return await base.PerformOperation(
                    workspace,
                    ResourceOperation.import,
                    [request.ResourceAddress],
                    request.ResourceId);
            }
        }
    }
}
