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

            public Handler(
                CasterContext db,
                IMapper mapper,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver)
            {
                _db = db;
                _mapper = mapper;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
            }

            public async Task<Workspace> Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var workspace = await _db.Workspaces.FindAsync(request.Id);

                await ValidateEntities(workspace, request.DirectoryId);

                _mapper.Map(request, workspace);
                await _db.SaveChangesAsync();
                return _mapper.Map<Workspace>(workspace);
            }

            private async Task ValidateEntities(Domain.Models.Workspace workspace, Guid directoryId)
            {
                if (workspace == null)
                    throw new EntityNotFoundException<Workspace>();

                var directory = await _db.Directories.FindAsync(directoryId);

                if (directory == null)
                    throw new EntityNotFoundException<Directory>();
            }
        }
    }
}
