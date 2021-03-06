// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using AutoMapper;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Runtime.Serialization;
using Caster.Api.Infrastructure.Exceptions;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Identity;
using System.Text.Json.Serialization;
using Caster.Api.Domain.Services;
using Microsoft.AspNetCore.Http;
using FluentValidation;
using System.Linq;
using Caster.Api.Domain.Models;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Caster.Api.Data.Extensions;

namespace Caster.Api.Features.Directories
{
    public class Import
    {
        [DataContract(Name="ImportDirectoryCommand")]
        public class Command : IRequest<ImportDirectoryResult>
        {
            [JsonIgnore]
            public Guid Id { get; set; }

            [DataMember]
            public IFormFile Archive { get; set; }

            [DataMember]
            public bool PreserveIds { get; set; }
        }

        public class ImportValidator : AbstractValidator<Command> {
            public ImportValidator() {
                RuleFor(x => x.Archive)
                    .NotNull().Must(BeAValidArchiveType)
                    .WithMessage($"File extension must be one of {string.Join(", ", ArchiveTypeHelpers.GetValidExtensions())}");
            }

            private bool BeAValidArchiveType(IFormFile file)
            {
                var isValid = false;

                foreach (var extension in ArchiveTypeHelpers.GetValidExtensions())
                {
                    if (file.FileName.ToLower().EndsWith(extension))
                    {
                        isValid = true;
                    }
                }

                return isValid;
            }
        }

        public class ImportDirectoryResult
        {
            /// <summary>
            /// A list of Files that were unable to be updated because
            /// they were locked or the current user does not have permission to lock them
            /// </summary>
            public string[] LockedFiles { get; set; }
        }

        public class Handler : IRequestHandler<Command, ImportDirectoryResult>
        {
            private readonly CasterContext _db;
            private readonly IMapper _mapper;
            private readonly IAuthorizationService _authorizationService;
            private readonly ClaimsPrincipal _user;
            private readonly IArchiveService _archiveService;
            private readonly IImportService _importService;
            private readonly IMediator _mediator;

            public Handler(
                CasterContext db,
                IMapper mapper,
                IAuthorizationService authorizationService,
                IIdentityResolver identityResolver,
                IArchiveService archiveService,
                IImportService importService,
                IMediator mediator)
            {
                _db = db;
                _mapper = mapper;
                _authorizationService = authorizationService;
                _user = identityResolver.GetClaimsPrincipal();
                _archiveService = archiveService;
                _importService = importService;
                _mediator = mediator;
            }

            public async Task<ImportDirectoryResult> Handle(Command request, CancellationToken cancellationToken)
            {
                if (!(await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRequirement())).Succeeded)
                    throw new ForbiddenException();

                var directory =  await _db.Directories
                    .SingleOrDefaultAsync(e => e.Id == request.Id, cancellationToken);

                if (directory == null)
                    throw new EntityNotFoundException<Directory>();

                Domain.Models.Directory extractedDirectory;

                using (var memStream = new System.IO.MemoryStream())
                {
                    await request.Archive.CopyToAsync(memStream, cancellationToken);
                    memStream.Position = 0;
                    extractedDirectory = _archiveService.ExtractDirectory(memStream, request.Archive.FileName);
                }

                var directories =  await _db.GetDirectoryWithChildren(directory.Id, cancellationToken);
                var importResult = await _importService.ImportDirectory(directory, extractedDirectory, request.PreserveIds);

                var entries = _db.GetUpdatedEntries();
                await _db.SaveChangesAsync(cancellationToken);
                await this.PublishEvents(entries);

                return _mapper.Map<ImportDirectoryResult>(importResult);
            }

            private async Task PublishEvents(EntityEntry[] entries)
            {
                foreach (var entry in entries)
                {
                    var evt = entry.ToEvent();

                    if (evt != null)
                    {
                        await _mediator.Publish(evt);
                    }
                }
            }
        }
    }
}
