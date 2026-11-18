// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// A host whose lock service runs a test's hook as a file's lock is asked for. Code that loads a file and then takes its lock
// asks for the lock after the load, so the hook runs in the window between them, which no request can be timed into.

using System;
using Caster.Api.Data;
using Caster.Api.Domain.Services;
using Caster.Api.Utilities.Synchronization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Caster.Api.Tests.Support;

/// <summary>
/// <see cref="CasterAppFactory"/> with <see cref="ILockService"/> replaced by <see cref="HookedLockService"/>. One per test
/// class (<c>IClassFixture</c>), over the same host database as <see cref="AiAppFactory"/>.
/// </summary>
public sealed class DeletingLockAppFactory : CasterAppFactory
{
    public HookedLockService Locks { get; } = new();

    protected override ITestDatabaseSession<CasterContext> HostDatabase() => DatabaseFixture.AiHostDatabase();

    /// <summary>The run-wide host still uses the Terraform directories when this class's host goes.</summary>
    protected override bool RemovesTerraformDirectories => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILockService>();
            services.AddSingleton<ILockService>(Locks);
        });
    }
}

/// <summary>The real lock service, which first runs <see cref="BeforeFileLock"/> with the id of each file whose lock is asked for.</summary>
public sealed class HookedLockService : ILockService
{
    private readonly LockService _inner = new();

    /// <summary>Run when a file's lock is asked for; null for none.</summary>
    public Action<Guid> BeforeFileLock { get; set; }

    public AsyncLock GetFileLock(Guid fileId)
    {
        BeforeFileLock?.Invoke(fileId);
        return _inner.GetFileLock(fileId);
    }

    public object GetHostLock(Guid hostId) => _inner.GetHostLock(hostId);
    public AsyncLock GetWorkspaceLock(Guid workspaceId) => _inner.GetWorkspaceLock(workspaceId);
    public AsyncLock GetPartitionLock(Guid partitionId) => _inner.GetPartitionLock(partitionId);
    public AsyncLock GetModuleSourceLock(string sourceName) => _inner.GetModuleSourceLock(sourceName);
    public void EnableWorkspaceLocking() => _inner.EnableWorkspaceLocking();
    public void DisableWorkspaceLocking() => _inner.DisableWorkspaceLocking();
    public bool IsWorkspaceLockingEnabled() => _inner.IsWorkspaceLockingEnabled();
}
