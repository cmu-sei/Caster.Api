// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
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

            /// <summary>
            /// A list of settings carried by the archive that could not be applied
            /// </summary>
            public string[] SkippedSettings { get; set; }

            /// <summary>
            /// A list of non-fatal problems found with the archive
            /// </summary>
            public string[] Warnings { get; set; }
        }

        public class Handler(
            ICasterAuthorizationService authorizationService,
            IMapper mapper,
            CasterContext dbContext,
            IArchiveService archiveService,
            IImportService importService,
            IMediator mediator) : BaseHandler<Command, ImportDirectoryResult>
        {
            public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
                await authorizationService.Authorize<Domain.Models.Directory>(request.Id, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

            public override async Task<ImportDirectoryResult> HandleRequest(Command request, CancellationToken cancellationToken)
            {
                var directory = await dbContext.Directories
                    .SingleOrDefaultAsync(e => e.Id == request.Id, cancellationToken);

                if (directory == null)
                    throw new EntityNotFoundException<Directory>();

                ArchiveExtractResult<Domain.Models.Directory> extracted;

                using (var memStream = new System.IO.MemoryStream())
                {
                    await request.Archive.CopyToAsync(memStream, cancellationToken);
                    memStream.Position = 0;
                    extracted = archiveService.ExtractDirectory(memStream, request.Archive.FileName);
                }

                var directories = await dbContext.GetDirectoryWithChildren(directory.Id, cancellationToken);
                var importResult = await importService.ImportDirectory(directory, extracted.Entity, request.PreserveIds, cancellationToken);

                importResult.SkippedSettings = extracted.SkippedSettings;
                importResult.Warnings = GetWarnings(extracted, directory.Name);

                var entries = dbContext.GetUpdatedEntries();
                await dbContext.SaveChangesAsync(cancellationToken);
                await this.PublishEvents(entries);

                return mapper.Map<ImportDirectoryResult>(importResult);
            }

            private static List<string> GetWarnings(ArchiveExtractResult extracted, string targetName)
            {
                var warnings = new List<string>(extracted.Warnings);
                var sourceName = extracted.Manifest?.Source?.Name;

                if (!string.IsNullOrEmpty(sourceName) && !sourceName.Equals(targetName, StringComparison.Ordinal))
                {
                    warnings.Add($"This archive was exported from '{sourceName}', which does not match '{targetName}'.");
                }

                return warnings;
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
