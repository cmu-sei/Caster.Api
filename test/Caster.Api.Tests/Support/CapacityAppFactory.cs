// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// A host whose capacity settings and hypervisor room a test class sets, for the apply checks. The run-wide
// host reads the room from an inventory with no connections, so it never has any room to check.

using Caster.Api.Data;
using Caster.Api.Domain.Services.Capacity;
using Caster.Api.Infrastructure.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Caster.Api.Tests.Support;

/// <summary>
/// <see cref="CasterAppFactory"/> with <see cref="Options"/> as the <c>Capacity</c> settings and
/// <see cref="Room"/> as the room on the hypervisors. One per test class (<c>IClassFixture</c>); a class's
/// tests run one at a time, so each may set both before it sends a request.
/// </summary>
public sealed class CapacityAppFactory : CasterAppFactory, IHypervisorRoomSource
{
    public CapacityOptions Options { get; set; } = new();

    public HypervisorRoom Room { get; set; } = new();

    HypervisorRoom IHypervisorRoomSource.Read(CapacityOptions options) => Room;

    protected override ITestDatabaseSession<CasterContext> HostDatabase() => DatabaseFixture.CapacityHostDatabase();

    protected override bool RemovesTerraformDirectories => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<CapacityOptions>();
            services.AddScoped(_ => Options);
            services.RemoveAll<IHypervisorRoomSource>();
            services.AddSingleton<IHypervisorRoomSource>(this);
        });
    }
}
