// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using AutoMapper;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Features.Workspaces.Interfaces;
using FluentValidation;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Features.Shared.Validators;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.Workspaces
{
    public class Edit
    {
        [DataContract(Name = "EditWorkspaceCommand")]
        public class Command : IRequest<Workspace>, IWorkspaceUpdateRequest
        {
            public Guid Id { get; set; }

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

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, Workspace>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Directory>(request.DirectoryId, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

            public override async Task<Workspace> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var workspace = await dbContext.Workspaces.FindAsync([request.Id], cancellationToken);

                if (workspace == null)
                    throw new EntityNotFoundException<Workspace>();

                mapper.Map(request, workspace);

                await dbContext.SaveChangesAsync(cancellationToken);
                return mapper.Map<Workspace>(workspace);
            }
        }
    }
}
