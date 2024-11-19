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
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using System.Security.Claims;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Features.Directories.Interfaces;
using FluentValidation;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Features.Shared.Validators;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Directories
{
    public class Edit
    {
        [DataContract(Name = "EditDirectoryCommand")]
        public class Command : IRequest<Directory>, IDirectoryUpdateRequest
        {
            public Guid Id { get; set; }

            /// <summary>
            /// Name of the directory.
            /// </summary>
            [DataMember]
            public string Name { get; set; }

            /// <summary>
            /// Id of the directory that will be the parent of this directory
            /// </summary>
            [DataMember]
            public Guid? ParentId { get; set; }

            /// <summary>
            /// The version of Terraform that will be set Workspaces created in this Directory.
            /// If not set, will traverse parents until a version is found.
            /// If still not set, the default version will be used.
            /// </summary>
            [DataMember]
            public string TerraformVersion { get; set; }

            /// <summary>
            /// Limit the number of concurrent operations as Terraform walks the graph. 
            /// If not set, will traverse parents until a value is found.
            /// If still not set, the Terraform default will be used.
            /// </summary>
            [DataMember]
            public int? Parallelism { get; set; }

            /// <summary>
            /// If set, the number of consecutive failed destroys in an Azure Workspace before 
            /// Caster will attempt to mitigate by removing azurerm_resource_group children from the state.
            /// If not set, will traverse parents until a value is found.
            /// </summary>
            [DataMember]
            public int? AzureDestroyFailureThreshold { get; set; }

            /// <summary>
            /// If false, ignore AzureDestroyFailureThreshold and set value to null for all new Workspaces in this Directory
            /// </summary>
            [DataMember]
            public bool AzureDestroyFailureThresholdEnabled { get; set; }
        }

        public class CommandValidator : AbstractValidator<Command>
        {
            public CommandValidator(IValidationService validationService, TerraformOptions options)
            {
                RuleFor(x => x.ParentId.Value).DirectoryExists(validationService).When(x => x.ParentId.HasValue);
                RuleFor(x => x.Parallelism.Value)
                    .ParalellismValidation(options)
                    .When(x => x.Parallelism.HasValue);
                RuleFor(x => x.AzureDestroyFailureThreshold.Value)
                    .AzureThresholdValidation()
                    .When(x => x.AzureDestroyFailureThreshold.HasValue);
            }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext db) : BaseEdit.Handler<Command, Directory>(db)
        {
            public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.Directory>(request.Id, [SystemPermissions.EditProjects], [ProjectPermissions.EditProject], cancellationToken);

            public override async Task<Directory> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var directory = await dbContext.Directories.FindAsync(request.Id);

                if (directory == null)
                    throw new EntityNotFoundException<Directory>();

                if (directory.ParentId != request.ParentId)
                {
                    await UpdatePaths(directory, request.ParentId);
                }

                mapper.Map(request, directory);

                await dbContext.SaveChangesAsync();
                return mapper.Map<Directory>(directory);
            }
        }
    }
}
