// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Caster.Api.Data;
using Caster.Api.Domain.Events;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Infrastructure.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Caster.Api.Features.Runs.EventHandlers
{
    public class RunAddedHandler : INotificationHandler<RunAdded>
    {
        private readonly CasterContext _db;
        private readonly DbContextOptions<CasterContext> _dbOptions;
        private readonly ILogger<RunAddedHandler> _logger;
        private readonly ITerraformService _terraformService;
        private readonly TerraformOptions _options;
        private readonly ILockService _lockService;
        private readonly IOutputService _outputService;
        private readonly IMediator _mediator;

        private System.Timers.Timer _timer;
        private Domain.Models.Plan _plan;
        private bool _timerComplete = false;
        private Output _output = null;

        public RunAddedHandler(
            CasterContext db,
            DbContextOptions<CasterContext> dbOptions,
            ILogger<RunAddedHandler> logger,
            ITerraformService terraformService,
            TerraformOptions options,
            ILockService lockService,
            IOutputService outputService,
            IMediator mediator)
        {
            _db = db;
            _dbOptions = dbOptions;
            _logger = logger;
            _terraformService = terraformService;
            _options = options;
            _lockService = lockService;
            _outputService = outputService;
            _mediator = mediator;
        }

        public async Task Handle(RunAdded notification, CancellationToken cancellationToken)
        {
            var run = await _db.Runs
                .Include(r => r.Workspace)
                    .ThenInclude(w => w.Directory)
                .Include(r => r.Workspace)
                    .ThenInclude(w => w.Host)
                .SingleOrDefaultAsync(x => x.Id == notification.RunId);

            try
            {
                var isError = await DoWork(run);

                _plan.Output = _output.Content;
                _plan.Status = !isError ? PlanStatus.Planned : PlanStatus.Failed;
                run.Status = !isError ? RunStatus.Planned : RunStatus.Failed;

                _output.SetCompleted();
                await _db.SaveChangesAsync();
                _outputService.RemoveOutput(_plan.Id);
                await _mediator.Publish(new RunUpdated(run.Id));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in {nameof(RunAddedHandler)}.Handle");
                run.Status = Domain.Models.RunStatus.Failed;
                await _db.SaveChangesAsync();
                await _mediator.Publish(new RunUpdated(run.Id));
            }
        }

        private async Task<bool> DoWork(Domain.Models.Run run)
        {
            var projectId = run.Workspace.Directory.ProjectId;
            var dynamicHost = run.Workspace.DynamicHost;

            Host host = null;

            _plan = await CreatePlan(run);

            var workspace = run.Workspace;
            var files = await _db.GetWorkspaceFiles(workspace, workspace.Directory);
            var workingDir = workspace.GetPath(_options.RootWorkingDirectory);

            if (dynamicHost)
            {
                // check if host already selected for this workspace
                if (workspace.HostId.HasValue)
                {
                    host = workspace.Host;
                }
                else
                {
                    // select a host. multiply by 1.0 to cast as double
                    host = await _db.Hosts
                        .Where(h => h.ProjectId == projectId && h.Enabled && !h.Development)
                        .OrderBy(h => (h.Machines.Count * 1.0 / h.MaximumMachines * 1.0) * 100.0).FirstOrDefaultAsync();
                }

                if (host == null)
                {
                    _output.AddLine("No Host could be found to use for this Plan");
                    return true;
                }
                else
                {
                    _output.AddLine($"Attempting to use Host {host.Name} for this Plan");
                }

                files.Add(host.GetHostFile());
            }

            await workspace.PrepareFileSystem(workingDir, files);

            _timer = new System.Timers.Timer(_options.OutputSaveInterval);
            _timer.Elapsed += OnTimedEvent;
            _timer.Start();

            bool isError = false;

            // Init
            var initResult = _terraformService.InitializeWorkspace(workspace, OutputHandler);
            isError = initResult.IsError;

            if (!isError)
            {
                if (run.IsDestroy && (run.Targets == null || !run.Targets.Any()) && initResult.Output.Contains("azurerm"))
                {
                    var resources = workspace.GetState().GetResources();

                    if (resources.Any(x => x.Type == "azurerm_resource_group"))
                    {
                        var targetResources = resources.Where(x => x.Type.StartsWith("azurerm") && x.Type != "azurerm_resource_group").ToArray();
                        var targets = targetResources.Select(x => x.Address).ToArray();
                        _terraformService.RemoveResources(workspace, targets, workspace.GetStatePath(workingDir, backupState: false));
                    }
                }

                // Plan
                var planResult = _terraformService.Plan(workspace, run.IsDestroy, run.Targets, OutputHandler);
                isError = planResult.IsError;
            }

            lock (_plan)
            {
                _timerComplete = true;
                _timer.Stop();
            }

            if (dynamicHost)
            {
                var result = _terraformService.Show(workspace);
                isError = result.IsError;

                if (!isError)
                {
                    var planOutput = JsonSerializer.Deserialize<PlanOutput>(result.Output, DefaultJsonSettings.Settings);
                    var addedMachines = planOutput.GetAddedMachines();

                    var hostLock = _lockService.GetHostLock(host.Id);

                    lock (hostLock)
                    {
                        var existingMachines = _db.HostMachines.Where(x => x.HostId == host.Id).ToList();
                        var addedCount = addedMachines.Count();

                        if (addedCount + existingMachines.Count < host.MaximumMachines)
                        {
                            List<HostMachine> newMachines = new List<HostMachine>();

                            foreach (var addedMachine in addedMachines)
                            {
                                var name = addedMachine.Address;

                                if (existingMachines.Any(m => m.WorkspaceId == workspace.Id && m.Name == name))
                                    continue;

                                newMachines.Add(new HostMachine
                                {
                                    HostId = host.Id,
                                    WorkspaceId = workspace.Id,
                                    Name = name
                                });
                            }

                            _db.HostMachines.AddRange(newMachines);
                            workspace.HostId = host.Id;
                            _db.SaveChanges();

                            _output.AddLine($"Allocated {addedCount} new machines to Host {host.Name}");
                        }
                        else
                        {
                            _output.AddLine($"{addedCount} new machines exceeds the maximum assigned to Host {host.Name}");
                            isError = true;
                        }
                    }
                }
            }

            return isError;
        }

        private async Task<Domain.Models.Plan> CreatePlan(Domain.Models.Run run)
        {
            var plan = new Domain.Models.Plan
            {
                RunId = run.Id,
                Status = Domain.Models.PlanStatus.Planning
            };

            run.Plan = plan;
            run.Status = Domain.Models.RunStatus.Planning;

            await _db.AddAsync(plan);
            await _db.SaveChangesAsync();

            _output = _outputService.GetOrAddOutput(plan.Id);
            await _mediator.Publish(new RunUpdated(run.Id));

            return plan;
        }

        private void OutputHandler(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null && _output != null)
            {
                _output.AddLine(e.Data);
            }
        }

        private void OnTimedEvent(Object source, ElapsedEventArgs e)
        {
            lock (_plan)
            {
                _timer.Stop();

                if (_timerComplete)
                {
                    return;
                }

                try
                {
                    using (var dbContext = new CasterContext(_dbOptions))
                    {
                        // Only update the Output field
                        dbContext.Plans.Attach(_plan);
                        _plan.Output = _output.Content;
                        dbContext.SaveChanges();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Timer");
                }
                finally
                {
                    _timer.Start();
                }
            }
        }
    }
}
