// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Concurrent;
using Caster.Api.Infrastructure.Exceptions;
using Caster.Api.Utilities.Synchronization;

namespace Caster.Api.Domain.Services
{
    public interface ILockService
    {
        Object GetHostLock(Guid hostId);
        AsyncLock GetFileLock(Guid fileId);
        AsyncLock GetWorkspaceLock(Guid workspaceId);
        void EnableWorkspaceLocking();
        void DisableWorkspaceLocking();
        bool IsWorkspaceLockingEnabled();
    }

    public class LockService : ILockService
    {
        private ConcurrentDictionary<Guid, Object> _hostLocks = new ConcurrentDictionary<Guid, object>();
        private ConcurrentDictionary<Guid, AsyncLock> _fileLocks = new ConcurrentDictionary<Guid, AsyncLock>();
        private ConcurrentDictionary<Guid, AsyncLock> _workspaceLocks = new ConcurrentDictionary<Guid, AsyncLock>();
        private bool _enableWorkspaceLocking = true;

        public LockService()
        {
        }

        public Object GetHostLock(Guid hostId)
        {
            return _hostLocks.GetOrAdd(hostId, x => { return new Object(); });
        }

        public AsyncLock GetFileLock(Guid fileId)
        {
            return _fileLocks.GetOrAdd(fileId, x => { return new AsyncLock(); });
        }

        #region Workspaces

        public AsyncLock GetWorkspaceLock(Guid workspaceId)
        {
            if (!_enableWorkspaceLocking)
            {
                throw new ConflictException("Workspace operations are currently disabled due to maintenance. They will be re-enabled shortly.");
            }

            return _workspaceLocks.GetOrAdd(workspaceId, x => { return new AsyncLock(); });
        }

        public void EnableWorkspaceLocking()
        {
            _enableWorkspaceLocking = true;
        }

        public void DisableWorkspaceLocking()
        {
            _enableWorkspaceLocking = false;
        }

        public bool IsWorkspaceLockingEnabled()
        {
            return _enableWorkspaceLocking;
        }

        #endregion
    }
}
