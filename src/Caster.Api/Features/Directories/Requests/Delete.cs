// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Caster.Api.Data;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using Caster.Api.Features.Directories.Interfaces;
using Caster.Api.Data.Extensions;

namespace Caster.Api.Features.Directories
{
    public class Delete
    {
        [DataContract(Name="DeleteDirectoryCommand")]
        public class Command : IRequest, IDirectoryDeleteRequest
        {
            public Guid Id { get; set; }
        }

        public class Handler : AsyncRequestHandler<Command>
        {
            private readonly CasterContext _db;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;

            public Handler(
                CasterContext db,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver,
                IMediator mediator)
            {
                _db = db;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
            }

            protected override async Task Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var directory = await _db.Directories.FindAsync(request.Id);

                if (directory == null)
                    throw new EntityNotFoundException<Directory>();

                var workspaces = await CheckForResources(directory);

                if (workspaces.Any())
                {
                    string errorMessage = "Cannot delete this Directory due to existing Resources in the following Workspaces:";

                    foreach (var workspace in workspaces)
                    {
                        errorMessage += $"\n Name: {workspace.Name}, Id: {workspace.Id} in Directory: {workspace.Directory.Name}, {workspace.DirectoryId}";
                    }

                    throw new ConflictException(errorMessage);
                }

                _db.Directories.Remove(directory);
                await _db.SaveChangesAsync(cancellationToken);
            }

            private async Task<Domain.Models.Workspace[]> CheckForResources(Domain.Models.Directory directory)
            {
                var directories = await _db.Directories
                    .GetChildren(directory, true)
                    .Include(d => d.Workspaces)
                    .ToArrayAsync();

                List<Domain.Models.Workspace> workspaces = new List<Domain.Models.Workspace>();

                foreach (var workspace in directories.SelectMany(d => d.Workspaces))
                {
                    if (workspace.GetState().GetResources().Any())
                    {
                        workspaces.Add(workspace);
                    }
                }

                return workspaces.ToArray();
            }
        }
    }
}
