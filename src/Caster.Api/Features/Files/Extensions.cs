// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.


using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.Files
{
    public static class FileExtensions
    {
        public static IQueryable<File> GetAll(
            this IQueryable<Domain.Models.File> query,
            bool includeDeleted,
            bool includeContent,
            Guid? directoryId = null)
        {
            if (directoryId.HasValue)
            {
                query = query.Where(f => f.DirectoryId == directoryId);
            }

            if(includeDeleted)
            {
                query = query.IgnoreQueryFilters();
            }

            IQueryable<File> returnQuery;

            if(includeContent)
            {
                returnQuery = query.ProjectToFileWithContent();
            }
            else
            {
                returnQuery = query.ProjectToFile();
            }

            return returnQuery;
        }
    }
}
