// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading.Tasks;
using AutoMapper;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;
using Caster.Api.Infrastructure.Options;
using Caster.Api.Domain.Services;
using Caster.Api.Infrastructure.Exceptions;
using Microsoft.Extensions.Logging;
using Caster.Api.Infrastructure.Identity;

namespace Caster.Api.Features.Resources
{
    public class BaseOperationHandler
    {
        protected enum ResourceOperation
        {
            taint,
            untaint,
            refresh
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

        protected async Task<Workspace> PerformOperation(Workspace workspace, ResourceOperation operation, string[] addresses)
        {
            using (var lockResult = await _lockService.GetWorkspaceLock(workspace.Id).LockAsync(0))
            {
                if (!lockResult.AcquiredLock)
                    throw new WorkspaceConflictException();

                if (!(await _db.AnyIncompleteRuns(workspace.Id)))
                {
                    return await this.OperationDoWork(workspace, operation, addresses);
                }
                else
                {
                    throw new WorkspaceConflictException();
                }
            }
        }

        private async Task<Workspace> OperationDoWork(Workspace workspace, ResourceOperation operation, string[] addresses)
        {
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
                                _logger.LogError(taintResult.Output);

                        }
                        break;
                    case ResourceOperation.refresh:
                        TerraformResult refreshResult = _terraformService.Refresh(workspace, statePath);
                        if (refreshResult.IsError) _logger.LogError(refreshResult.Output);
                        break;
                }

                await workspace.RetrieveState(workingDir);
                await _db.SaveChangesAsync();
                workspace.CleanupFileSystem(_terraformOptions.RootWorkingDirectory);
            }

            return workspace;
        }
    }
}
