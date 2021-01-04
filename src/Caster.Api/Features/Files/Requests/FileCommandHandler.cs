// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading.Tasks;
using Caster.Api.Data;
using AutoMapper;
using Caster.Api.Infrastructure.Exceptions;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Identity;
using System.Threading;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Extensions;

namespace Caster.Api.Features.Files
{
    public interface IFileCommand
    {
        Guid Id { get; set; }
    }

    public abstract class FileCommandHandler
    {
        protected readonly CasterContext _db;
        protected readonly IMapper _mapper;
        protected readonly IAuthorizationService _authorizationService;
        protected readonly ClaimsPrincipal _user;
        protected readonly ILockService _lockService;
        protected readonly IGetFileQuery _fileQuery;
        protected readonly IIdentityResolver _identityResolver;

        public FileCommandHandler(
            CasterContext db,
            IMapper mapper,
            IAuthorizationService authorizationService,
            IIdentityResolver identityResolver,
            ILockService lockService,
            IGetFileQuery fileQuery)
        {
            _db = db;
            _mapper = mapper;
            _authorizationService = authorizationService;
            _user = identityResolver.GetClaimsPrincipal();
            _lockService = lockService;
            _fileQuery = fileQuery;
            _identityResolver = identityResolver;
        }

        protected async Task<File> Handle(IFileCommand request, CancellationToken cancellationToken)
        {
            await this.Authorize();

            using (var lockResult = await _lockService.GetFileLock(request.Id).LockAsync(0))
            {
                if (!lockResult.AcquiredLock)
                    throw new FileConflictException();

                var file = await _db.Files.FindAsync(request.Id);

                if (file == null)
                    throw new EntityNotFoundException<File>();

                await this.PerformOperation(file);

                await _db.SaveChangesAsync(cancellationToken);
            }

            return await _fileQuery.ExecuteAsync(request.Id);
        }

        protected virtual async Task Authorize() {
            if (!(await _authorizationService.AuthorizeAsync(
                _user, null, new ContentDeveloperRequirement())).Succeeded)
            {
                throw new ForbiddenException();
            }
        }

        protected abstract Task PerformOperation(Domain.Models.File file);

    }
}

