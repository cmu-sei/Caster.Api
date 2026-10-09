// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading.Tasks;
using Caster.Api.Data;
using AutoMapper;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.EntityFrameworkCore;
using Caster.Api.Data.Extensions;
using Caster.Api.Features.Shared;
using MediatR;
using System.Threading;

namespace Caster.Api.Features.Directories
{
    public abstract class BaseEdit
    {
        public abstract class Handler<TRequest, TResponse> : BaseHandler<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
        {
            protected readonly CasterContext dbContext;

            public Handler(CasterContext db)
            {
                dbContext = db;
            }

            protected async Task UpdatePaths(Domain.Models.Directory directory, Guid? parentId, CancellationToken cancellationToken)
            {
                string parentPath = null;
                string oldPath = directory.Path;

                if (parentId.HasValue)
                {
                    var parentDirectory = await dbContext.Directories.FindAsync([parentId.Value], cancellationToken);

                    if (parentDirectory == null)
                        throw new EntityNotFoundException<Directory>("Parent Directory Not Found");

                    if (parentDirectory.ProjectId != directory.ProjectId)
                        throw new ConflictException("Parent and child Directories must be in the same Project");

                    if (parentDirectory.Id == directory.Id || parentDirectory.Path.StartsWith(directory.Path, StringComparison.Ordinal))
                        throw new ConflictException("A Directory cannot be its own parent or a child of its descendants");

                    parentPath = parentDirectory.Path;
                }

                var descendants = await dbContext.Directories.GetChildren(directory, false).ToListAsync(cancellationToken);

                directory.SetPath(parentPath);

                foreach (var desc in descendants)
                {
                    desc.Path = desc.Path.Replace(oldPath, directory.Path);
                }
            }
        }
    }
}
