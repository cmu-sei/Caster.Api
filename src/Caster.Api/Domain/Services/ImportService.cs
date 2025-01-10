// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Extensions;
using Caster.Api.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using static Caster.Api.Utilities.Synchronization.AsyncLock;

namespace Caster.Api.Domain.Services
{
    public interface IImportService
    {
        Task<ImportResult> ImportProject(Project existingProject, Project importedProject, bool preserveIds, CancellationToken cancellationToken);
        Task<ImportResult> ImportDirectory(Directory existingDirectory, Directory importedDirectory, bool preserveIds, CancellationToken cancellationToken);
    }

    public class ImportService : IImportService
    {
        private readonly ILockService _lockService;
        private readonly CasterContext _db;
        private readonly ICasterAuthorizationService _authorizationService;
        private readonly Guid _userId;

        public ImportService(
            ILockService lockService,
            CasterContext db,
            IIdentityResolver identityResolver,
            ICasterAuthorizationService authorizationService)
        {
            _lockService = lockService;
            _db = db;
            _authorizationService = authorizationService;
            _userId = identityResolver.GetClaimsPrincipal().GetId();
        }

        public async Task<ImportResult> ImportProject(Project existingProject, Project importedProject, bool preserveIds, CancellationToken cancellationToken)
        {
            List<File> lockedFiles = new List<File>();
            List<AsyncLockResult> fileLocks = new List<AsyncLockResult>();

            try
            {
                foreach (var directory in importedProject.Directories.Where(x => !x.ParentId.HasValue))
                {
                    var existingDir = existingProject.Directories.FirstOrDefault(x => x.Name.Equals(directory.Name));

                    if (existingDir == null)
                    {
                        if (preserveIds && directory.Id != Guid.Empty)
                        {
                            existingDir = new Directory(directory.Name, id: directory.Id);
                        }
                        else
                        {
                            existingDir = new Directory(directory.Name);
                        }

                        _db.Entry(existingDir).State = EntityState.Added;
                        existingDir.ProjectId = existingProject.Id;
                        existingProject.Directories.Add(existingDir);
                    }

                    lockedFiles.AddRange((await this.ImportDirectoryInternal(existingDir, directory, preserveIds, fileLocks, cancellationToken)).LockedFiles);
                }
            }
            finally
            {
                foreach (var lockResult in fileLocks)
                {
                    lockResult.Dispose();
                }
            }

            return new ImportResult
            {
                LockedFiles = lockedFiles
            };
        }

        public async Task<ImportResult> ImportDirectory(Directory existingDir, Domain.Models.Directory dirToImport, bool preserveIds, CancellationToken cancellationToken)
        {
            ImportResult result = new ImportResult();
            List<AsyncLockResult> fileLocks = new List<AsyncLockResult>();

            try
            {
                result = await this.ImportDirectoryInternal(existingDir, dirToImport, preserveIds, fileLocks, cancellationToken);
            }
            finally
            {
                foreach (var lockResult in fileLocks)
                {
                    lockResult.Dispose();
                }
            }

            return result;
        }

        private async Task<ImportResult> ImportDirectoryInternal(
            Directory existingDir,
            Directory dirToImport,
            bool preserveIds,
            List<AsyncLockResult> fileLocks,
            CancellationToken cancellationToken)
        {
            var lockedFiles = new List<File>();

            foreach (var workspace in dirToImport.Workspaces)
            {
                var dbWorkspace = existingDir.Workspaces.FirstOrDefault(x => x.Name.Equals(workspace.Name));
                var workspaceToUse = dbWorkspace;

                if (dbWorkspace == null)
                {
                    var newWorkspace = new Workspace(workspace.Name, existingDir);
                    existingDir.Workspaces.Add(newWorkspace);
                    workspaceToUse = newWorkspace;
                }

                foreach (var file in workspace.Files)
                {
                    var dbFile = workspaceToUse.Files.FirstOrDefault(x => x.Name.Equals(file.Name));

                    if (dbFile == null)
                    {
                        workspaceToUse.Files.Add(file);
                        existingDir.Files.Add(file);
                        file.Save(
                            _userId,
                            await CanLock(file.Id, cancellationToken),
                            bypassLock: true);
                    }
                    else
                    {
                        var fileUpdateResult = await this.UpdateFile(dbFile, file, cancellationToken);
                        fileLocks.Add(fileUpdateResult.LockResult);

                        if (fileUpdateResult.UnableToLock)
                        {
                            lockedFiles.Add(dbFile);
                        }
                    }
                }
            }

            foreach (var file in dirToImport.Files)
            {
                var dbFile = existingDir.Files.FirstOrDefault(x => x.Name.Equals(file.Name));

                if (dbFile == null)
                {
                    existingDir.Files.Add(file);
                    file.Save(
                        _userId,
                        await CanLock(file.Id, cancellationToken),
                        bypassLock: true);
                }
                else
                {
                    var fileUpdateResult = await this.UpdateFile(dbFile, file, cancellationToken);
                    fileLocks.Add(fileUpdateResult.LockResult);

                    if (fileUpdateResult.UnableToLock)
                    {
                        lockedFiles.Add(dbFile);
                    }
                }
            }

            foreach (var directory in dirToImport.Children)
            {
                var dbChildDir = existingDir.Children.FirstOrDefault(x => x.Name.Equals(directory.Name));
                var childDirToUse = dbChildDir;

                if (dbChildDir == null)
                {
                    Guid? id = null;

                    if (preserveIds && directory.Id != Guid.Empty)
                    {
                        id = directory.Id;
                    }

                    var newDir = new Directory(directory.Name, existingDir, id);
                    existingDir.Children.Add(newDir);
                    childDirToUse = newDir;
                    _db.Entry(newDir).State = EntityState.Added;
                }

                var l = (await this.ImportDirectoryInternal(childDirToUse, directory, preserveIds, fileLocks, cancellationToken)).LockedFiles;

                lockedFiles.AddRange(l);
            }

            return new ImportResult
            {
                LockedFiles = lockedFiles
            };
        }

        private class FileUpdateResult
        {
            public bool FileUpdated { get; set; }
            public bool UnableToLock { get; set; }
            public AsyncLockResult LockResult { get; set; }
        }

        private async Task<FileUpdateResult> UpdateFile(Domain.Models.File dbFile, Domain.Models.File file, CancellationToken cancellationToken)
        {
            var result = new FileUpdateResult();

            result.LockResult = await _lockService.GetFileLock(dbFile.Id).LockAsync(0);

            // Don't need to update or throw error if contents haven't changed
            if (!dbFile.Content.Equals(file.Content))
            {
                var canLock = await CanLock(file.Id, cancellationToken);
                if (!result.LockResult.AcquiredLock)
                {
                    result.UnableToLock = true;
                    result.FileUpdated = false;
                }
                else if (dbFile.CanLock(_userId, canLock))
                {
                    dbFile.Content = file.Content;
                    dbFile.Save(
                        _userId,
                        canLock,
                        bypassLock: true);

                    result.FileUpdated = true;
                    result.UnableToLock = false;
                }
                else
                {
                    result.FileUpdated = false;
                    result.UnableToLock = true;
                }
            }

            return result;
        }

        private async Task<bool> CanLock(Guid fileId, CancellationToken cancellationToken)
        {
            return await _authorizationService.Authorize<Domain.Models.File>(fileId, [SystemPermission.LockFiles], [ProjectPermission.LockFiles], cancellationToken);
        }
    }
}
