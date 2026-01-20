// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Caster.Api.Data;
using Caster.Api.Domain.Events;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Options;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Caster.Api.Features.Applies.EventHandlers
{
    public class ApplyAddedHandler : INotificationHandler<ApplyAdded>
    {
        private readonly CasterContext _db;
        private readonly DbContextOptions<CasterContext> _dbOptions;
        private readonly ILogger<ApplyAddedHandler> _logger;
        private readonly ITerraformService _terraformService;
        private readonly TerraformOptions _options;
        private readonly IMediator _mediator;
        private readonly IOutputService _outputService;

        private System.Timers.Timer _timer;
        private Domain.Models.Apply _apply;
        private bool _timerComplete = false;
        private Output _output = null;

        public ApplyAddedHandler(
            CasterContext db,
            DbContextOptions<CasterContext> dbOptions,
            ILogger<ApplyAddedHandler> logger,
            ITerraformService terraformService,
            TerraformOptions options,
            IMediator mediator,
            IOutputService outputService)
        {
            _db = db;
            _dbOptions = dbOptions;
            _logger = logger;
            _terraformService = terraformService;
            _options = options;
            _mediator = mediator;
            _outputService = outputService;
        }

        public async Task Handle(ApplyAdded notification, CancellationToken cancellationToken)
        {
            _apply = await _db.Applies
                .Include(a => a.Run)
                    .ThenInclude(r => r.Workspace)
                .SingleOrDefaultAsync(x => x.Id == notification.ApplyId);

            string workingDir = string.Empty;
            var stateRetrieved = false;
            var planExists = true;

            try
            {
                _output = _outputService.GetOrAddOutput(_apply.Id);

                if (_apply.Status == ApplyStatus.Queued)
                {
                    // Update status
                    _apply.Status = Domain.Models.ApplyStatus.Applying;
                    _apply.Run.Status = Domain.Models.RunStatus.Applying;
                    await this.UpdateApply();
                }

                workingDir = _apply.Run.Workspace.GetPath(_options.RootWorkingDirectory);

                _timer = new System.Timers.Timer(_options.OutputSaveInterval);
                _timer.Elapsed += OnTimedEvent;
                _timer.Start();

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in {nameof(ApplyAddedHandler)}.Handle");
                _apply.Status = Domain.Models.ApplyStatus.Failed;
                _apply.Run.Status = Domain.Models.RunStatus.Failed;
                await this.UpdateApply();
                return;
            }

            try
            {
                planExists = _apply.Run.Workspace.PlanExists(workingDir);

                if (!planExists)
                {
                    _apply.Output = "Plan file not found. Please try to Plan again.";
                    _apply.Status = ApplyStatus.Failed;
                    _apply.Run.Status = RunStatus.Failed;
                }
                else
                {
                    var result = await _terraformService.Apply(_apply.Run.Workspace, _apply.Status == ApplyStatus.PostApply, OutputHandler, async (string output) =>
                    {
                        _apply.Status = ApplyStatus.PostApply;
                        _apply.Output = output;
                        await _db.SaveChangesAsync();
                    });

                    bool isError = result.IsError;

                    lock (_apply)
                    {
                        _timerComplete = true;
                        _timer.Stop();
                    }

                    _apply.Output = result.Output;
                    _apply.Status = !isError ? ApplyStatus.Applied : ApplyStatus.Failed;
                    _apply.Run.Status = !isError ? RunStatus.Applied : RunStatus.Failed;

                    stateRetrieved = await this.RetrieveState(workingDir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in {nameof(ApplyAddedHandler)}.Handle");
            }
            finally
            {
                if (planExists && !stateRetrieved)
                {
                    _apply.Status = _apply.Status == ApplyStatus.Applied ? ApplyStatus.Applied_StateError : ApplyStatus.Failed_StateError;
                    _apply.Run.Status = _apply.Run.Status == RunStatus.Applied ? RunStatus.Applied_StateError : RunStatus.Failed_StateError;
                }

                await this.UpdateApply();
            }

            try
            {
                _output.SetCompleted();
                _outputService.RemoveOutput(_apply.Id);

                if (stateRetrieved)
                {
                    await _mediator.Publish(new ApplyCompleted(_apply.Run.Workspace));
                    _apply.Run.Workspace.CleanupFileSystem(_options.RootWorkingDirectory);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error Cleaning up Apply {_apply.Id}");
            }
        }

        private async Task<bool> RetrieveState(string workingDir)
        {
            var count = 1;
            bool stateRetrieved = false;

            while (count <= _options.StateRetryCount)
            {
                try
                {
                    stateRetrieved = await _apply.Run.Workspace.RetrieveState(workingDir);

                    if (stateRetrieved)
                    {
                        await this.UpdateApply();
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error Retrieving State for Workspace {_apply.Run.WorkspaceId}");
                }

                count++;
                await Task.Delay(_options.StateRetryIntervalSeconds * 1000);
            }

            return stateRetrieved;
        }

        private async Task UpdateApply()
        {
            await _db.SaveChangesAsync();
            await _mediator.Publish(new RunUpdated(_apply.RunId));
        }

        private void OutputHandler(string data)
        {
            if (data is not null && _output is not null)
            {
                _output.AddLine(data);
            }
        }

        private void OnTimedEvent(Object source, ElapsedEventArgs e)
        {
            lock (_apply)
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
                        dbContext.Applies.Attach(_apply);
                        _apply.Output = _output.Content;

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
