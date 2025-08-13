// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using AutoMapper;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Workspaces.Interfaces;
using FluentValidation;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Features.Shared;
using Caster.Api.Features.Shared.Validators;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

namespace Caster.Api.Features.Workspaces
{
    public class Create
    {
        [DataContract(Name = "CreateWorkspaceCommand")]
        public class Command : IRequest<Workspace>, IWorkspaceUpdateRequest
        {
            /// <summary>
            /// The Name of the Workspace
            /// </summary>
            [DataMember]
            public string Name { get; set; }

            /// <summary>
            /// The Id of the Directory of the Workspace
            /// </summary>
            [DataMember]
            public Guid DirectoryId { get; set; }

            /// <summary>
            /// True if this Workspace will be dynamically assigned a Host on first Run
            /// </summary>
            [DataMember]
            public bool DynamicHost { get; set; }

            /// <summary>
            /// The version of Terraform that will be used for Runs in this Workspace.
            /// If null or empty, the default version will be used.
            /// </summary>
            [DataMember]
            public string TerraformVersion { get; set; }

            /// <summary>
            /// Limit the number of concurrent operations as Terraform walks the graph.
            /// If null, the Terraform default will be used.
            /// </summary>
            [DataMember]
            public int? Parallelism { get; set; }

            /// <summary>
            /// If set, the number of consecutive failed destroys in an Azure Workspace before
            /// Caster will attempt to mitigate by removing azurerm_resource_group children from the state.
            /// </summary>
            [DataMember]
            public int? AzureDestroyFailureThreshold { get; set; }
        }

        public class CommandValidator : AbstractValidator<Command>
        {
            public CommandValidator(IValidationService validationService, TerraformOptions options)
            {
                RuleFor(x => x.DirectoryId).DirectoryExists(validationService);
                RuleFor(x => x.Parallelism.Value)
                    .ParalellismValidation(options)
                    .When(x => x.Parallelism.HasValue);
                RuleFor(x => x.AzureDestroyFailureThreshold.Value)
                    .AzureThresholdValidation()
                    .When(x => x.AzureDestroyFailureThreshold.HasValue);
            }
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            IMapper mapper,
            CasterContext dbContext,
            TerraformOptions terraformOptions,
            TelemetryService telemetryService) : BaseHandler<Command, Workspace>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Directory>(request.DirectoryId, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

            public override async Task<Workspace> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var workspace = mapper.Map<Domain.Models.Workspace>(request);
                workspace = await SetCascadedProperties(workspace, request, cancellationToken);

                dbContext.Workspaces.Add(workspace);
                await dbContext.SaveChangesAsync(cancellationToken);
                return mapper.Map<Workspace>(workspace);
            }

            private async Task<Domain.Models.Workspace> SetCascadedProperties(Domain.Models.Workspace workspace, Command request, CancellationToken ct)
            {
                if (!request.Parallelism.HasValue && string.IsNullOrEmpty(request.TerraformVersion))
                {
                    // Load parent directories from database
                    var directory = await dbContext.GetDirectoryWithAncestors(workspace.DirectoryId, ct);

                    workspace.TerraformVersion = !string.IsNullOrEmpty(request.TerraformVersion) ?
                        request.TerraformVersion :
                        GetTerraformVersion(directory);

                    workspace.Parallelism = request.Parallelism.HasValue ?
                        request.Parallelism.Value :
                        GetParallelism(directory);

                    workspace.AzureDestroyFailureThreshold = request.AzureDestroyFailureThreshold.HasValue ?
                        request.AzureDestroyFailureThreshold.Value :
                        GetAzureDestroyThreshold(directory);
                }

                return workspace;
            }

            private string GetTerraformVersion(Domain.Models.Directory directory)
            {
                if (!string.IsNullOrEmpty(directory.TerraformVersion))
                {
                    return directory.TerraformVersion;
                }
                else if (directory.Parent != null)
                {
                    return this.GetTerraformVersion(directory.Parent);
                }
                else
                {
                    return terraformOptions.DefaultVersion;
                }
            }

            private int? GetParallelism(Domain.Models.Directory directory)
            {
                if (directory.Parallelism.HasValue)
                {
                    return directory.Parallelism;
                }
                else if (directory.Parent != null)
                {
                    return GetParallelism(directory.Parent);
                }
                else
                {
                    return null;
                }
            }

            private int? GetAzureDestroyThreshold(Domain.Models.Directory directory)
            {
                if (!directory.AzureDestroyFailureThresholdEnabled)
                {
                    return null;
                }
                else if (directory.AzureDestroyFailureThreshold.HasValue)
                {
                    return directory.AzureDestroyFailureThreshold;
                }
                else if (directory.Parent != null)
                {
                    return GetAzureDestroyThreshold(directory.Parent);
                }
                else
                {
                    return terraformOptions.AzureDestroyFailureThreshhold;
                }
            }
        }
    }
}
