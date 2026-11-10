// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.Files
{
    public interface IGetFileQuery
    {
        Task<File> ExecuteAsync(Guid fileId, bool includeContent = true);
    }

    public class GetFileQuery : IGetFileQuery
    {
        private readonly CasterContext _db;

        private ConcurrentDictionary<Guid, File> _cache = new ConcurrentDictionary<Guid, File>();

        public GetFileQuery(CasterContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Gets a single Domain.Models.File by Id from the database and projects it into a File
        /// Will retrieve result from cache if available.
        /// This is intended to be Scoped to avoid multiple db round-trips in the same API call, not a persistent global cache
        /// </summary>
        public async Task<File> ExecuteAsync(Guid fileId, bool includeContent = true)
        {
            File file;

            if (!_cache.TryGetValue(fileId, out file))
            {
                var query = _db.Files
                    .Where(f => f.Id == fileId)
                    .IgnoreQueryFilters();

                file = await (includeContent ? query.ProjectToFileWithContent() : query.ProjectToFile())
                    .FirstOrDefaultAsync();

                _cache.TryAdd(fileId, file);
            }

            // The cache keeps whichever shape was read first, and the line below edits the cached object,
            // so a read without content followed by one with it, in the same request, has no content.
            if (!includeContent)
            {
                file.Content = null;
            }

            return file;
        }
    }
}

