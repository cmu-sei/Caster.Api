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
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Identity;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Caster.Api.Features.Files.Interfaces;
using System.Text.Json.Serialization;

namespace Caster.Api.Features.Files
{
    public class Edit
    {
        [DataContract(Name="EditFileCommand")]
        public class Command : FileUpdateRequest, IRequest<File>, IFileCommand
        {
            [JsonIgnore]
            public Guid Id { get; set; }

            /// <summary>
            /// Name of the file.
            /// </summary>
            [DataMember]
            public string Name { get; set; }

            /// <summary>
            /// ID of the directory this file is under.
            /// </summary>
            [DataMember]
            public Guid DirectoryId { get; set; }

            /// <summary>
            /// An optional Workspace to assign this File to
            /// </summary>
            [DataMember]
            public Guid? WorkspaceId { get; set; }

            /// <summary>
            /// The full contents of the file.
            /// </summary>
            [DataMember]
            public override string Content { get; set; }
        }

        public class Handler : FileCommandHandler, IRequestHandler<Command, File>
        {
            private Command _request { get; set; }

            public Handler(
                CasterContext db,
                IMapper mapper,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver,
                ILockService lockService,
                IGetFileQuery fileQuery)
                : base(db, mapper, authorizationService, identityResolver, lockService, fileQuery) {}

            public async Task<File> Handle(Command request, CancellationToken cancellationToken)
            {
                _request = request;
                return await base.Handle(request, cancellationToken);
            }

            protected override async Task PerformOperation(Domain.Models.File file)
            {
                await ValidateEntities(file, _request.DirectoryId, _request.WorkspaceId);

                file = _mapper.Map(_request, file);
                file.Save(_user.GetId(), isAdmin: (await _identityResolver.IsAdminAsync()));
            }

            private async Task ValidateEntities(Domain.Models.File file, Guid directoryId, Guid? workspaceId)
            {
                var directory = await _db.Directories.FindAsync(directoryId);

                if (directory == null)
                    throw new EntityNotFoundException<Directory>();

                if (workspaceId.HasValue)
                {
                    var workspace = await _db.Workspaces.FindAsync(workspaceId.Value);

                    if (workspace == null)
                        throw new EntityNotFoundException<Workspace>();
                }
            }

        }
    }
}

