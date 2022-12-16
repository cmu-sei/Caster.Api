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
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Features.Workspaces.Interfaces;
using FluentValidation;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Extensions;

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
        }

        public class CommandValidator : AbstractValidator<Command>
        {
            public CommandValidator(IValidationService validationService)
            {
                RuleFor(x => x.DirectoryId).DirectoryExists(validationService);
                RuleFor(x => x.Parallelism)
                    .GreaterThan(0)
                    .LessThan(25);
            }
        }

        public class Handler : IRequestHandler<Command, Workspace>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;
            private readonly TerraformOptions _terraformOptions;

            public Handler(
                CasterContext db,
                IMapper mapper,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver,
                TerraformOptions terraformOptions)
            {
                _db = db;
                _mapper = mapper;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
                _terraformOptions = terraformOptions;
            }

            public async Task<Workspace> Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var workspace = _mapper.Map<Domain.Models.Workspace>(request);
                workspace = await SetCascadedProperties(workspace, request, cancellationToken);

                _db.Workspaces.Add(workspace);
                await _db.SaveChangesAsync(cancellationToken);
                return _mapper.Map<Workspace>(workspace);
            }

            private async Task<Domain.Models.Workspace> SetCascadedProperties(Domain.Models.Workspace workspace, Command request, CancellationToken ct)
            {
                if (!request.Parallelism.HasValue && string.IsNullOrEmpty(request.TerraformVersion))
                {
                    // Load parent directories from database
                    var directory = await _db.GetDirectoryWithAncestors(workspace.DirectoryId, ct);

                    workspace.TerraformVersion = !string.IsNullOrEmpty(request.TerraformVersion) ?
                        request.TerraformVersion :
                        GetTerraformVersion(directory);

                    workspace.Parallelism = request.Parallelism.HasValue ?
                        request.Parallelism.Value :
                        GetParallelism(directory);
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
                    return _terraformOptions.DefaultVersion;
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
        }
    }
}
