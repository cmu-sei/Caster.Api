// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using AutoMapper;
using System.Runtime.Serialization;
using Microsoft.EntityFrameworkCore;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Domain.Models;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Features.Directories.Interfaces;
using FluentValidation;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Features.Shared.Validators;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Npgsql;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.Directories
{
    public class Create
    {
        [DataContract(Name = "CreateDirectoryCommand")]
        public class Command : IRequest<Directory>, IDirectoryUpdateRequest
        {
            /// <summary>
            /// Name of the directory.
            /// </summary>
            [DataMember]
            public string Name { get; set; }

            /// <summary>
            /// ID of the project this directory is under.
            /// </summary>
            [DataMember]
            public Guid ProjectId { get; set; }

            /// <summary>
            /// ID of the directory this directory is under.
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
                RuleFor(x => x.ProjectId).ProjectExists(validationService);
                RuleFor(x => x.Parallelism.Value)
                    .ParalellismValidation(options)
                    .When(x => x.Parallelism.HasValue);
                RuleFor(x => x.AzureDestroyFailureThreshold.Value)
                    .AzureThresholdValidation()
                    .When(x => x.AzureDestroyFailureThreshold.HasValue);
            }
        }

        public class Handler(ICasterAuthorizationService authorizationService, IMapper mapper, CasterContext dbContext) : BaseHandler<Command, Directory>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Project>(request.ProjectId, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

            public override async Task<Directory> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var directory = mapper.Map<Domain.Models.Directory>(request);
                await SetPath(directory);

                try
                {
                    dbContext.Directories.Add(directory);
                    await dbContext.SaveChangesAsync(cancellationToken);


                    return mapper.Map<Directory>(directory);
                }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx)
                {

                    // Handle specific PostgreSQL errors
                    switch (pgEx.SqlState)
                    {
                        case "23505": // unique_violation
                            throw new InvalidOperationException($"A Directory with the ID '{directory.Id}' already exists.", ex);
                        case "23503": // foreign_key_violation
                            var constraintName = pgEx.ConstraintName ?? "unknown";
                            if (constraintName.Contains("ProjectId", StringComparison.OrdinalIgnoreCase))
                            {
                                throw new InvalidOperationException($"Invalid ProjectId '{request.ProjectId}'. The Project does not exist.", ex);
                            }
                            if (constraintName.Contains("ParentId", StringComparison.OrdinalIgnoreCase))
                            {
                                throw new InvalidOperationException($"Invalid ParentId '{request.ParentId}'. The parent Directory does not exist.", ex);
                            }
                            throw new InvalidOperationException($"Foreign key constraint violated: {constraintName}. Please verify all referenced entities exist.", ex);
                        case "23514": // check_violation
                            throw new InvalidOperationException($"Data validation failed: {pgEx.MessageText}", ex);
                        default:
                            throw new InvalidOperationException($"Database error creating Directory: {pgEx.MessageText}", ex);
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"An unexpected error occurred while creating the Directory: {ex.Message}", ex);
                }
            }

            private async Task SetPath(Domain.Models.Directory directory)
            {
                directory.Id = Guid.NewGuid();

                if (!directory.ParentId.HasValue)
                {
                    directory.SetPath();
                }
                else
                {
                    var parentDirectory = await dbContext.Directories.FindAsync(directory.ParentId);

                    if (parentDirectory == null)
                        throw new EntityNotFoundException<Directory>("Parent Directory not found");

                    if (parentDirectory.ProjectId != directory.ProjectId)
                        throw new ConflictException("Parent and child Directories must be in the same Project");

                    directory.SetPath(parentDirectory.Path);
                }
            }
        }
    }
}
