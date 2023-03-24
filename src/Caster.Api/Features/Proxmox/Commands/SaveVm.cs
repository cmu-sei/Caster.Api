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
using Caster.Api.Features.Shared;
using System.Collections.Generic;
using Caster.Api.Features.Resources;

namespace Caster.Api.Features.Proxmox
{
    public class SaveVm
    {
        [DataContract(Name = "ProxmoxSaveVmCommand")]
        public class Command : IRequest<ResourceCommandResult>
        {
            /// <summary>
            /// ID of the Workspace.
            /// </summary>
            [JsonIgnore]
            public Guid WorkspaceId { get; set; }

            /// <summary>
            /// Address of the Resource
            /// </summary>
            [DataMember]
            public string ResourceAddress { get; set; }
        }

        public class Handler : BaseOperationHandler, IRequestHandler<Command, ResourceCommandResult>
        {
            private IProxmoxService _proxmoxService { get; set; }

            public Handler(
                CasterContext db,
                IMapper mapper,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver,
                TerraformOptions terraformOptions,
                ITerraformService terraformService,
                ILockService lockService,
                ILogger<Handler> logger,
                IProxmoxService proxmoxService) :
            base(db, mapper, authorizationService, identityResolver, terraformOptions, terraformService, lockService, logger)
            {
                _proxmoxService = proxmoxService;
            }

            public async Task<ResourceCommandResult> Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new FullRightsRequirement())).Succeeded)
                    throw new ForbiddenException();

                var workspace = await base.GetWorkspace(request.WorkspaceId);

                var result = await base.PerformOperation(
                    workspace,
                    ResourceOperation.remove,
                    new string[] { request.ResourceAddress });

                return result;
            }

            protected override async Task<string[]> PreDoWork(Workspace workspace, string[] addresses)
            {
                var resource = workspace.GetState().GetResources().Where(x => x.Address == addresses.FirstOrDefault()).FirstOrDefault();
                var templateId = resource.Attributes.GetProperty("clone")[0].GetProperty("vm_id").GetInt32();

                return await _proxmoxService.SaveVm(Int32.Parse(resource.Id), templateId);
            }
        }
    }
}
