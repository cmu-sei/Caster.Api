// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Runs.EventHandlers;
using Caster.Api.Hubs;
using Caster.Api.Tests.Support;
using Crucible.Common.EntityEvents.Events;
using Microsoft.AspNetCore.SignalR;
using RunView = Caster.Api.Features.Runs.Run;

namespace Caster.Api.Tests.Features.Runs.EventHandlers;

/// <summary>
/// The run broadcasts go to two audiences at once, the workspace's group and the workspaces admin group,
/// through <c>Clients.Groups(...)</c>, which the shared <see cref="HubRecorder{THub}"/> records under a
/// combined key it does not expose. These drive the handlers directly over a hub context the test owns.
/// </summary>
public class RunSignalRHandlerTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task A_created_run_is_sent_to_its_workspace_and_the_workspaces_admin_group()
    {
        var (run, hub, proxy) = await SeedRunAndHub();

        await new RunCreatedSignalRHandler(Db, TestMapper.Mapper, hub).Handle(new EntityCreated<Run>(run), Ct);

        hub.Clients.Received(1).Groups(Arg.Is<IReadOnlyList<string>>(x =>
            x.SequenceEqual(new[] { run.WorkspaceId.ToString(), nameof(HubGroups.WorkspacesAdmin) })));
        var sent = Assert.Single(proxy.ReceivedCalls());
        Assert.Equal("RunCreated", sent.GetArguments()[0]);
        Assert.Equal(run.Id, Assert.IsType<RunView>(((object[])sent.GetArguments()[1])[0]).Id);
    }

    [Fact]
    public async Task A_deleted_run_sends_its_id_to_its_workspace_and_the_workspaces_admin_group()
    {
        var (run, hub, proxy) = await SeedRunAndHub();

        await new RunDeletedSignalRHandler(hub).Handle(new EntityDeleted<Run>(run), Ct);

        hub.Clients.Received(1).Groups(Arg.Is<IReadOnlyList<string>>(x =>
            x.SequenceEqual(new[] { run.WorkspaceId.ToString(), nameof(HubGroups.WorkspacesAdmin) })));
        var sent = Assert.Single(proxy.ReceivedCalls());
        Assert.Equal("RunDeleted", sent.GetArguments()[0]);
        Assert.Equal(run.Id, ((object[])sent.GetArguments()[1])[0]);
    }

    private async Task<(Run Run, IHubContext<ProjectHub> Hub, IClientProxy Proxy)> SeedRunAndHub()
    {
        var project = TestData.Project();
        var directory = TestData.Directory(project);
        var workspace = TestData.Workspace(directory);
        var run = TestData.Run(workspace);
        await Seed(project, directory, workspace, run);

        var proxy = Substitute.For<IClientProxy>();
        var hub = Substitute.For<IHubContext<ProjectHub>>();
        hub.Clients.Groups(Arg.Any<IReadOnlyList<string>>()).Returns(proxy);

        return (run, hub, proxy);
    }
}
