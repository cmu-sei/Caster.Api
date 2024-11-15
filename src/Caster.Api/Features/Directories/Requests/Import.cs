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
using Caster.Api.Infrastructure.Authorization;
using System.Text.Json.Serialization;
using Caster.Api.Domain.Services;
using Microsoft.AspNetCore.Http;
using FluentValidation;
using Caster.Api.Domain.Models;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Caster.Api.Data.Extensions;
using Caster.Api.Features.Shared;

namespace Caster.Api.Features.Directories
{
    public class Import
    {
        [DataContract(Name = "ImportDirectoryCommand")]
        public class Command : IRequest<ImportDirectoryResult>
        {
            [JsonIgnore]
            public Guid Id { get; set; }

            [DataMember]
            public IFormFile Archive { get; set; }

            [DataMember]
            public bool PreserveIds { get; set; }
        }

        public class ImportValidator : AbstractValidator<Command>
        {
            public ImportValidator()
            {
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

        public class Handler(
            ICasterAuthorizationService authorizationService,
            IMapper mapper,
            CasterContext dbContext,
            IArchiveService archiveService,
            IImportService importService,
            IMediator mediator) : BaseHandler<Command, ImportDirectoryResult>
        {
            public override async Task Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Directory>(request.Id, [SystemPermissions.EditProjects], [ProjectPermissions.EditProject], cancellationToken);

            public override async Task<ImportDirectoryResult> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var directory = await dbContext.Directories
                    .SingleOrDefaultAsync(e => e.Id == request.Id, cancellationToken);

                if (directory == null)
                    throw new EntityNotFoundException<Directory>();

                Domain.Models.Directory extractedDirectory;

                using (var memStream = new System.IO.MemoryStream())
                {
                    await request.Archive.CopyToAsync(memStream, cancellationToken);
                    memStream.Position = 0;
                    extractedDirectory = archiveService.ExtractDirectory(memStream, request.Archive.FileName);
                }

                var directories = await dbContext.GetDirectoryWithChildren(directory.Id, cancellationToken);
                var importResult = await importService.ImportDirectory(directory, extractedDirectory, request.PreserveIds);

                var entries = dbContext.GetUpdatedEntries();
                await dbContext.SaveChangesAsync(cancellationToken);
                await this.PublishEvents(entries);

                return mapper.Map<ImportDirectoryResult>(importResult);
            }

            private async Task PublishEvents(EntityEntry[] entries)
            {
                foreach (var entry in entries)
                {
                    var evt = entry.ToEvent();

                    if (evt != null)
                    {
                        await mediator.Publish(evt);
                    }
                }
            }
        }
    }
}
