// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using Caster.Api.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Data.Extensions
{
    public static class DirectoryExtensions
    {
        public static IQueryable<Directory> GetChildren(this IQueryable<Directory> query, Directory directory, bool includeSelf)
        {
            var pattern = $"{directory.Path}%";
            query = query.Where(d => EF.Functions.Like(d.Path, pattern));

            if (!includeSelf)
            {
                query = query.Where(d => d.Id != directory.Id);
            }

            return query;
        }
    }
}
