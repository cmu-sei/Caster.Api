// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading.Tasks;
using AutoMapper;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Exceptions;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Text.Json;
using Caster.Api.Features.Shared;
using MediatR;

namespace Caster.Api.Features.Resources
{
    public abstract class BaseOperationHandler<TRequest, TResponse>(
        IMapper mapper,
        CasterContext dbContext,
        ILockService lockService,
        TerraformOptions terraformOptions,
        ITerraformService terraformService) : BaseHandler<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        protected enum ResourceOperation
        {
            taint,
            untaint,
            refresh,
            remove,
            import,
            output
        }

        protected async Task<ResourceCommandResult> PerformOperation(Workspace workspace, ResourceOperation operation, string[] addresses, string args = null)
        {
            using (var lockResult = await lockService.GetWorkspaceLock(workspace.Id).LockAsync(0))
            {
                if (!lockResult.AcquiredLock)
                    throw new WorkspaceConflictException();

                if (!await dbContext.AnyIncompleteRuns(workspace.Id))
                {
                    return await this.OperationDoWork(workspace, operation, addresses, args);
                }
                else
                {
                    throw new WorkspaceConflictException();
                }
            }
        }

        private async Task<ResourceCommandResult> OperationDoWork(Workspace workspace, ResourceOperation operation, string[] addresses, string args)
        {
            var errors = new List<string>();
            JsonElement? outputs = null;
            var workingDir = workspace.GetPath(terraformOptions.RootWorkingDirectory);
            var files = await dbContext.GetWorkspaceFiles(workspace, workspace.Directory);
            await workspace.PrepareFileSystem(workingDir, files);

            var initResult = await terraformService.InitializeWorkspace(workspace, null);

            if (!initResult.IsError)
            {
                TerraformResult result = null;

                switch (operation)
                {
                    case ResourceOperation.taint:
                    case ResourceOperation.untaint:
                        foreach (string address in addresses)
                        {
                            TerraformResult taintResult = null;

                            switch (operation)
                            {
                                case ResourceOperation.taint:
                                    taintResult = await terraformService.Taint(workspace, address);
                                    break;
                                case ResourceOperation.untaint:
                                    taintResult = await terraformService.UntaintAsync(workspace, address);
                                    break;
                            }

                            if (taintResult != null && taintResult.IsError)
                            {
                                errors.Add(taintResult.Output);
                            }
                        }
                        break;
                    case ResourceOperation.refresh:
                        result = await terraformService.RefreshAsync(workspace);
                        break;
                    case ResourceOperation.remove:
                        result = await terraformService.RemoveResourcesAsync(workspace, addresses);
                        break;
                    case ResourceOperation.import:
                        result = await terraformService.Import(workspace, addresses[0], args);
                        break;
                    case ResourceOperation.output:
                        result = await terraformService.GetOutputsAsync(workspace);
                        outputs = JsonDocument.Parse(result.Output).RootElement;
                        break;
                }

                if (result != null && result.IsError)
                {
                    errors.Add(result.Output);
                }

                await workspace.RetrieveState(workingDir);
                await dbContext.SaveChangesAsync();
                workspace.CleanupFileSystem(terraformOptions.RootWorkingDirectory);
            }

            return new ResourceCommandResult
            {
                Resources = mapper.Map<Resource[]>(workspace.GetState().GetResources(), opts => opts.ExcludeMembers(nameof(Resource.Attributes))),
                Errors = errors.ToArray(),
                Outputs = outputs
            };
        }

        protected async Task<Workspace> GetWorkspace(Guid id)
        {
            var workspace = await dbContext.Workspaces
                .Include(w => w.Directory)
                .Where(w => w.Id == id)
                .FirstOrDefaultAsync();

            if (workspace == null) throw new EntityNotFoundException<Workspace>();

            return workspace;
        }
    }
}
