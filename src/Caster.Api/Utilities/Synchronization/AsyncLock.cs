// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Caster.Api.Utilities.Synchronization
{
    public class AsyncLock
    {
        private readonly SemaphoreSlim _semaphore;

        public AsyncLock()
        {
            _semaphore = new SemaphoreSlim(1, 1);
        }

        public TaskWrapper<AsyncLockResult> LockAsync(int timeout) =>
            new TaskWrapper<AsyncLockResult>(LockInternalAsync(timeout));

        private async Task<AsyncLockResult> LockInternalAsync(int timeout)
        {
            bool acquiredLock = await _semaphore.WaitAsync(timeout);
            return new AsyncLockResult(_semaphore, acquiredLock);
        }

        public class AsyncLockResult : IDisposable
        {
            public bool AcquiredLock { get; }

            private readonly SemaphoreSlim _semaphore;

            public AsyncLockResult(SemaphoreSlim semaphore, bool acquiredLock)
            {
                _semaphore = semaphore;
                this.AcquiredLock = acquiredLock;
            }

            public void Dispose()
            {
                if (this.AcquiredLock)
                    _semaphore.Release();
            }
        }
    }

    public struct TaskWrapper<T>
    {
        private readonly Task<T> _task;

        public TaskWrapper(Task<T> task)
        {
            _task = task;
        }

        public TaskAwaiter<T> GetAwaiter() => _task.GetAwaiter();
    }
}
