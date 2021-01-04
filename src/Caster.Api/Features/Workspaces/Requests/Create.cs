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
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Features.Workspaces.Interfaces;
using FluentValidation;
using Caster.Api.Infrastructure.Options;

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
        }

        public class CommandValidator : AbstractValidator<Command>
        {
            public CommandValidator(IValidator<IWorkspaceUpdateRequest> baseValidator)
            {
                Include(baseValidator);
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

                var directory = await this.GetDirectory(request.DirectoryId, cancellationToken);

                var workspace = _mapper.Map<Domain.Models.Workspace>(request);
                workspace.TerraformVersion = !string.IsNullOrEmpty(request.TerraformVersion) ?
                    request.TerraformVersion :
                    await GetTerraformVersion(directory.Id, cancellationToken);

                await _db.Workspaces.AddAsync(workspace, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                return _mapper.Map<Workspace>(workspace);
            }

            private async Task<Directory> GetDirectory(Guid id, CancellationToken ct)
            {
                var directory = await _db.Directories.FindAsync(id);

                if (directory == null)
                    throw new EntityNotFoundException<Directory>();

                return directory;
            }

            private async Task<string> GetTerraformVersion(Guid id, CancellationToken ct)
            {
                // Load parent directories from database
                var directory = await _db.GetDirectoryWithAncestors(id, ct);
                return this.GetTerraformVersion(directory);
            }

            private string GetTerraformVersion(Directory directory)
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
        }
    }
}
