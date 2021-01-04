// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.


using System.Linq;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.Directories
{
    public static class DirectoryExtensions
    {
        public static IQueryable<Directory> Expand(this IQueryable<Domain.Models.Directory> query, IConfigurationProvider configuration, bool includeRelated, bool includeFileContent)
        {
            IQueryable<Directory> newQuery;

            if (includeRelated)
            {
                if (includeFileContent)
                {
                    newQuery = query.ProjectTo<Directory>(configuration, dest => dest.Files, dest => dest.Files.Select(f => f.Content), dest => dest.Workspaces);
                }
                else
                {
                    newQuery = query.ProjectTo<Directory>(configuration, dest => dest.Files, dest => dest.Workspaces);
                }
            }
            else
            {
                newQuery = query.ProjectTo<Directory>(configuration);
            }

            return newQuery;
        }
    }
}
