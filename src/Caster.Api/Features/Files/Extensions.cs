// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.


using System;
using System.Linq;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.Files
{
    public static class FileExtensions
    {
        public static IQueryable<File> GetAll(
            this IQueryable<Domain.Models.File> query,
            IConfigurationProvider configurationProvider,
            bool includeDeleted,
            bool includeContent,
            Guid? directoryId = null)
        {
            IQueryable<Domain.Models.File> initialQuery = query;

            if (directoryId.HasValue)
            {
                initialQuery = initialQuery.Where(f => f.DirectoryId == directoryId);
            }

            if(includeDeleted)
            {
                initialQuery = query.IgnoreQueryFilters();
            }

            IQueryable<File> returnQuery;

            if(includeContent)
            {
                returnQuery = initialQuery.ProjectTo<File>(configurationProvider, dest => dest.Content);
            }
            else
            {
                returnQuery = initialQuery.ProjectTo<File>(configurationProvider);
            }

            return returnQuery;
        }
    }
}
