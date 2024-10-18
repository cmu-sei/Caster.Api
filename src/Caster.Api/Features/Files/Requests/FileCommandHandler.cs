// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Domain.Services;
using System.Threading;
using Caster.Api.Features.Shared;
using MediatR;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Files
{
    public interface IFileCommand
    {
        Guid Id { get; set; }
    }

    public abstract class FileCommandHandler<TRequest, TResponse>(
        CasterContext dbContext,
        ILockService lockService,
        IGetFileQuery fileQuery,
        ICasterAuthorizationService authorizationService) : BaseHandler<TRequest, TResponse>
        where TRequest : IRequest<TResponse>, IFileCommand
        where TResponse : File
    {
        protected ICasterAuthorizationService AuthorizationService => authorizationService;

        public override async Task<TResponse> HandleRequest(TRequest request, CancellationToken cancellationToken)
        {
            using (var lockResult = await lockService.GetFileLock(request.Id).LockAsync(0))
            {
                if (!lockResult.AcquiredLock)
                    throw new FileConflictException();

                var file = await dbContext.Files.FindAsync(request.Id);

                if (file == null)
                    throw new EntityNotFoundException<File>();

                await this.PerformOperation(file, cancellationToken);

                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return (TResponse)await fileQuery.ExecuteAsync(request.Id);
        }

        protected async Task<bool> CanLock(Guid fileId, CancellationToken cancellationToken)
        {
            return await authorizationService.Authorize<Domain.Models.File>(fileId, [SystemPermission.LockFiles], [ProjectPermission.LockFiles], cancellationToken);
        }

        protected abstract Task PerformOperation(Domain.Models.File file, CancellationToken cancellationToken);
    }
}

