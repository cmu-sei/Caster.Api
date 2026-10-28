// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Features.Shared;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Features.Shared.Validators;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Infrastructure.Identity;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.Files;

public class ApplyAiDraft
{
    [DataContract(Name = "ApplyAiTerraformDraftCommand")]
    public record Command : IRequest<File[]>
    {
        [JsonIgnore]
        public Guid DirectoryId { get; set; }

        /// <summary>
        /// The reviewed files of an AI Terraform draft, as the designer left them.
        /// </summary>
        [DataMember]
        public List<FileChange> Files { get; init; } = [];
    }

    public record FileChange
    {
        public string Name { get; init; }
        public string Content { get; init; }

        /// <summary>
        /// The Directory's file to replace, from the draft; empty to create a file.
        /// </summary>
        public Guid? ExistingFileId { get; init; }

        /// <summary>
        /// The draft's hash of the replaced file's content. The file is not replaced if it has changed since.
        /// </summary>
        public string ExistingContentHash { get; init; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.DirectoryId).DirectoryExists(validationService);
            RuleFor(x => x.Files).NotEmpty();
            RuleForEach(x => x.Files).ChildRules(file =>
            {
                file.RuleFor(f => f.Name).FileNameValidation();
                file.RuleFor(f => f.Content).NotNull();
                file.RuleFor(f => f.ExistingContentHash).NotEmpty().When(f => f.ExistingFileId.HasValue);
            });
            RuleFor(x => x.Files)
                .Must(files => files.Select(f => f.Name).Distinct().Count() == files.Count)
                .WithMessage("Each file of the draft must have a different name.");
        }
    }

    public class Handler(
        ICasterAuthorizationService authorizationService,
        CasterContext dbContext,
        ILockService lockService,
        IIdentityResolver identityResolver,
        IMapper mapper) : BaseHandler<Command, File[]>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Directory>(request.DirectoryId, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

        public override async Task<File[]> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var userId = identityResolver.GetId();
            var canLock = await authorizationService.Authorize<Directory>(request.DirectoryId, [SystemPermission.LockFiles], [ProjectPermission.LockFiles], cancellationToken);
            var directory = await dbContext.Directories.SingleAsync(d => d.Id == request.DirectoryId, cancellationToken);
            var directoryFiles = await dbContext.Files
                .Where(f => f.DirectoryId == request.DirectoryId && f.WorkspaceId == null)
                .ToListAsync(cancellationToken);

            // every file is checked before any is saved, so a draft is applied whole or not at all
            var locks = new List<IDisposable>();
            try
            {
                var saved = new List<Domain.Models.File>();
                foreach (var change in request.Files)
                {
                    if (change.ExistingFileId.HasValue)
                    {
                        var file = directoryFiles.SingleOrDefault(f => f.Id == change.ExistingFileId.Value)
                            ?? throw new ConflictException($"{change.Name} is no longer a file of this Directory. Draft again.");
                        var fileLock = await lockService.GetFileLock(file.Id).LockAsync(0);
                        locks.Add(fileLock);
                        if (!fileLock.AcquiredLock)
                            throw new FileConflictException();
                        if (AiTerraformDraftBuilder.ContentHash(file.Content) != change.ExistingContentHash)
                            throw new ConflictException($"{file.Name} has changed since the draft was written. Draft again.");
                        if (file.LockedById.HasValue && file.LockedById != userId)
                            throw new ConflictException($"{file.Name} is being edited by another user.");
                        if (file.AdministrativelyLocked && !canLock)
                            throw new FileAdminLockedException();
                        if (file.Name != change.Name && directoryFiles.Any(f => f.Id != file.Id && f.Name == change.Name))
                            throw new ConflictException($"This Directory already has a file named {change.Name}.");
                        saved.Add(file);
                    }
                    else if (directoryFiles.Any(f => f.Name == change.Name))
                    {
                        throw new ConflictException($"This Directory has had a file named {change.Name} added since the draft was written. Draft again.");
                    }
                }

                var created = new List<Domain.Models.File>();
                foreach (var change in request.Files)
                {
                    if (change.ExistingFileId.HasValue)
                    {
                        var file = saved.Single(f => f.Id == change.ExistingFileId.Value);
                        // the editor saves a file its user holds the lock of, so take it for the save when nobody holds it
                        var heldBefore = file.LockedById == userId;
                        if (!heldBefore)
                            file.Lock(userId, canLock);
                        file.Name = change.Name;
                        file.Content = change.Content;
                        file.Save(userId, canLock);
                        if (!heldBefore)
                            file.Unlock(userId);
                    }
                    else
                    {
                        var file = new Domain.Models.File
                        {
                            Id = Guid.NewGuid(),
                            Name = change.Name,
                            Content = change.Content,
                            Directory = directory
                        };
                        file.Save(userId, canLock, bypassLock: true);
                        dbContext.Files.Add(file);
                        created.Add(file);
                    }
                }

                await dbContext.SaveChangesAsync(cancellationToken);

                var ids = saved.Concat(created).Select(f => f.Id).ToList();
                return await dbContext.Files
                    .Where(f => ids.Contains(f.Id))
                    .OrderBy(f => f.Name)
                    .ProjectTo<File>(mapper.ConfigurationProvider)
                    .ToArrayAsync(cancellationToken);
            }
            finally
            {
                foreach (var fileLock in locks)
                    fileLock.Dispose();
            }
        }
    }
}
