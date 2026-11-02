// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Features.Files;
using Caster.Api.Features.Shared;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Infrastructure.Identity;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using FileCreate = Caster.Api.Features.Files.Create;
using FileEdit = Caster.Api.Features.Files.Edit;
using FileLock = Caster.Api.Features.Files.Lock;
using FileUnlock = Caster.Api.Features.Files.Unlock;
using WorkspacePartialEdit = Caster.Api.Features.Workspaces.PartialEdit;

namespace Caster.Api.Features.AiAssist;

public class Apply
{
    [DataContract(Name = "ApplyAiAssistantChangesCommand")]
    public record Command : IRequest<AiAssistApplyResult>
    {
        [JsonIgnore]
        public Guid DirectoryId { get; set; }

        /// <summary>
        /// The proposed changes the planner kept, as Ask AI proposed them.
        /// </summary>
        [DataMember]
        public List<AiAssistChange> Changes { get; init; } = [];
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator(IValidationService validationService)
        {
            RuleFor(x => x.DirectoryId).DirectoryExists(validationService);
            RuleFor(x => x.Changes).NotEmpty();
            RuleFor(x => x.Changes).Must(c => c == null || c.Count <= 100).WithMessage("Apply at most 100 changes at once.");
        }
    }

    /// <summary>
    /// Makes each change through the request a person making it by hand would send (create, lock, edit and unlock a file;
    /// edit a workspace), with that request's own permission, lock and validation rules, after checking it again against
    /// the Directory as it is now. One change failing does not stop the rest.
    /// </summary>
    public class Handler(
        ICasterAuthorizationService authorizationService,
        CasterContext dbContext,
        IMediator mediator,
        IIdentityResolver identityResolver,
        ITerraformService terraformService) : BaseHandler<Command, AiAssistApplyResult>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize<Directory>(request.DirectoryId, [SystemPermission.EditProjects], [ProjectPermission.EditProject], cancellationToken);

        public override async Task<AiAssistApplyResult> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var result = new AiAssistApplyResult();
            var userId = identityResolver.GetId();
            foreach (var change in request.Changes.Where(c => c != null))
            {
                var outcome = new AiAssistChangeResult { Op = change.Op, Label = AiAssistBuilder.Label(change) };
                try
                {
                    outcome.Error = change.Op switch
                    {
                        AiAssistOps.FileAdd => await AddFile(request.DirectoryId, change, outcome, cancellationToken),
                        AiAssistOps.FileEdit => await EditFile(request.DirectoryId, change, userId, outcome, cancellationToken),
                        AiAssistOps.WorkspaceVersion => await SetVersion(request.DirectoryId, change, outcome, cancellationToken),
                        _ => "Ask AI cannot make this kind of change."
                    };
                }
                catch (Exception ex) when (ex is IApiException or FluentValidation.ValidationException)
                {
                    outcome.Error = Message(ex);
                }
                finally
                {
                    // a request that failed part way may leave changes it never saved; the next change must not save them
                    dbContext.ChangeTracker.Clear();
                }
                outcome.Applied = outcome.Error == null;
                result.Results.Add(outcome);
            }
            return result;
        }

        private async Task<string> AddFile(Guid directoryId, AiAssistChange change, AiAssistChangeResult outcome, CancellationToken ct)
        {
            if (!AiTerraformDraftBuilder.IsValidFileName(change.FileName))
                return "File names may only use letters, digits, dots, underscores and dashes.";
            if (await DirectoryFiles(directoryId).AnyAsync(f => f.Name == change.FileName, ct))
                return $"A file named {change.FileName} has been added to this directory since this was proposed. Ask again.";

            var file = await mediator.Send(new FileCreate.Command { DirectoryId = directoryId, Name = change.FileName, Content = change.Content ?? "" }, ct);
            outcome.FileId = file.Id;
            return null;
        }

        private async Task<string> EditFile(Guid directoryId, AiAssistChange change, Guid userId, AiAssistChangeResult outcome, CancellationToken ct)
        {
            var file = change.FileId.HasValue
                ? await DirectoryFiles(directoryId).Where(f => f.Id == change.FileId.Value).Select(f => new { f.Id, f.Name, f.LockedById }).SingleOrDefaultAsync(ct)
                : null;
            if (file == null)
                return $"{change.FileName} is no longer a file of this directory. Ask again.";
            outcome.FileId = file.Id;
            if (file.LockedById.HasValue && file.LockedById != userId)
                return $"{file.Name} is being edited by another user.";

            // the editor saves a file its user holds the lock of: take it for the save when nobody holds it, as the editor does
            var heldBefore = file.LockedById == userId;
            if (!heldBefore)
                await mediator.Send(new FileLock.Command { Id = file.Id }, ct);
            try
            {
                // checked under the lock, so nobody can save it between the check and the edit
                var current = await dbContext.Files.AsNoTracking().Where(f => f.Id == file.Id).Select(f => f.Content).SingleAsync(ct);
                if (AiTerraformDraftBuilder.ContentHash(current) != change.ExistingContentHash)
                    return $"{file.Name} has changed since this was proposed, so it was not replaced. Ask again.";

                await mediator.Send(new FileEdit.Command { Id = file.Id, Name = file.Name, Content = change.Content ?? "" }, ct);
                return null;
            }
            finally
            {
                if (!heldBefore)
                {
                    dbContext.ChangeTracker.Clear();
                    await mediator.Send(new FileUnlock.Command { Id = file.Id }, ct);
                }
            }
        }

        private async Task<string> SetVersion(Guid directoryId, AiAssistChange change, AiAssistChangeResult outcome, CancellationToken ct)
        {
            var workspace = change.WorkspaceId.HasValue
                ? await dbContext.Workspaces.AsNoTracking().Where(w => w.Id == change.WorkspaceId.Value && w.DirectoryId == directoryId)
                    .Select(w => new { w.Id, w.TerraformVersion }).SingleOrDefaultAsync(ct)
                : null;
            if (workspace == null)
                return $"{change.WorkspaceName ?? "The workspace"} is no longer a workspace of this directory. Ask again.";
            outcome.WorkspaceId = workspace.Id;
            if (!string.Equals(workspace.TerraformVersion ?? "", change.ExistingTerraformVersion ?? "", StringComparison.Ordinal))
                return $"{change.WorkspaceName}'s Terraform version has changed since this was proposed. Ask again.";
            // the workspace request's own version check is not run when it is sent from here, so it is made here
            if (string.IsNullOrWhiteSpace(change.TerraformVersion) || !terraformService.IsValidVersion(change.TerraformVersion))
                return $"Caster has no Terraform {change.TerraformVersion}. Ask an administrator to add it.";

            await mediator.Send(new WorkspacePartialEdit.Command { Id = workspace.Id, TerraformVersion = change.TerraformVersion }, ct);
            return null;
        }

        // the files a workspace of the Directory is built from that belong to the Directory itself
        private IQueryable<Domain.Models.File> DirectoryFiles(Guid directoryId) =>
            dbContext.Files.AsNoTracking().Where(f => f.DirectoryId == directoryId && f.WorkspaceId == null);

        private static string Message(Exception ex) => ex switch
        {
            ForbiddenException => "You do not have permission to make this change.",
            Infrastructure.Exceptions.ValidationException validation when validation.Errors?.Count > 0 =>
                string.Join(" ", validation.Errors.Values.SelectMany(e => e)),
            FluentValidation.ValidationException validation => string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)),
            _ => ex.Message
        };
    }
}
