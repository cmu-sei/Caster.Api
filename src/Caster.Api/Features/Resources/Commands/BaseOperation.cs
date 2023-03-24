// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading.Tasks;
using AutoMapper;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.Extensions.Logging;
using Caster.Api.Infrastructure.Identity;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Text.Json;

namespace Caster.Api.Features.Resources
{
    public abstract class BaseOperationHandler
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

        protected readonly CasterContext _db;
        protected readonly IMapper _mapper;
        protected readonly IAuthorizationService _authorizationService;
        protected readonly ClaimsPrincipal _user;
        protected readonly TerraformOptions _terraformOptions;
        protected readonly ITerraformService _terraformService;
        protected readonly ILockService _lockService;
        protected readonly ILogger<BaseOperationHandler> _logger;

        public BaseOperationHandler(
            CasterContext db,
            IMapper mapper,
            IAuthorizationService authorizationService,
            IIdentityResolver identityResolver,
            TerraformOptions terraformOptions,
            ITerraformService terraformService,
            ILockService lockService,
            ILogger<BaseOperationHandler> logger)
        {
            _db = db;
            _mapper = mapper;
            _authorizationService = authorizationService;
            _user = identityResolver.GetClaimsPrincipal();
            _terraformOptions = terraformOptions;
            _terraformService = terraformService;
            _lockService = lockService;
            _logger = logger;
        }

        protected async Task<ResourceCommandResult> PerformOperation(Workspace workspace, ResourceOperation operation, string[] addresses, string args = null)
        {
            using (var lockResult = await _lockService.GetWorkspaceLock(workspace.Id).LockAsync(0))
            {
                if (!lockResult.AcquiredLock)
                    throw new WorkspaceConflictException();

                if (!await _db.AnyIncompleteRuns(workspace.Id))
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
            var workingDir = workspace.GetPath(_terraformOptions.RootWorkingDirectory);
            var files = await _db.GetWorkspaceFiles(workspace, workspace.Directory);
            await workspace.PrepareFileSystem(workingDir, files);

            var initResult = _terraformService.InitializeWorkspace(workspace, null);

            var statePath = string.Empty;

            if (!workspace.IsDefault)
            {
                statePath = workspace.GetStatePath(workingDir, backupState: false);
            }

            if (!initResult.IsError)
            {
                TerraformResult result = null;

                errors.AddRange(await this.PreDoWork(workspace, addresses));

                if (!errors.Any())
                {
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
                                        taintResult = _terraformService.Taint(workspace, address, statePath);
                                        break;
                                    case ResourceOperation.untaint:
                                        taintResult = _terraformService.Untaint(workspace, address, statePath);
                                        break;
                                }

                                if (taintResult != null && taintResult.IsError)
                                {
                                    errors.Add(taintResult.Output);
                                }
                            }
                            break;
                        case ResourceOperation.refresh:
                            result = _terraformService.Refresh(workspace, statePath);
                            break;
                        case ResourceOperation.remove:
                            result = _terraformService.RemoveResources(workspace, addresses, statePath);
                            break;
                        case ResourceOperation.import:
                            result = _terraformService.Import(workspace, addresses[0], args, statePath);
                            break;
                        case ResourceOperation.output:
                            result = _terraformService.GetOutputs(workspace, statePath);
                            outputs = JsonDocument.Parse(result.Output).RootElement;
                            break;
                    }

                    if (result != null && result.IsError)
                    {
                        errors.Add(result.Output);
                    }
                }

                await workspace.RetrieveState(workingDir);
                await _db.SaveChangesAsync();
                workspace.CleanupFileSystem(_terraformOptions.RootWorkingDirectory);
            }

            return new ResourceCommandResult
            {
                Resources = _mapper.Map<Resource[]>(workspace.GetState().GetResources(), opts => opts.ExcludeMembers(nameof(Resource.Attributes))),
                Errors = errors.ToArray(),
                Outputs = outputs
            };
        }

        protected async Task<Workspace> GetWorkspace(Guid id)
        {
            var workspace = await _db.Workspaces
                .Include(w => w.Directory)
                .Where(w => w.Id == id)
                .FirstOrDefaultAsync();

            if (workspace == null) throw new EntityNotFoundException<Workspace>();

            return workspace;
        }

        protected virtual async Task<string[]> PreDoWork(Workspace workspace, string[] addresses)
        {
            return Array.Empty<string>();
        }
    }
}
