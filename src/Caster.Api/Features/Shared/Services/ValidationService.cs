// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading.Tasks;
using System;
using Caster.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.Shared.Services;

public interface IValidationService
{
    Task<bool> DirectoryExists(Guid directoryId);
    Task<bool> DesignExists(Guid designId);
    Task<bool> ModuleExists(Guid moduleId);
    Task<bool> ProjectExists(Guid projectId);
    Task<bool> PartitionExists(Guid partitionId);
    Task<bool> PoolExists(Guid poolId);
    Task<bool> WorkspaceExists(Guid workspaceId);
    Task<bool> VlanExists(Guid vlanId);
    Task<bool> UserExists(Guid userId);
    Task<bool> GroupExists(Guid groupId);
    Task<bool> ProjectRoleExists(Guid roleId);
    Task<bool> SystemRoleExists(Guid roleId);
}

public class ValidationService : IValidationService
{
    private readonly CasterContext _dbContext;

    public ValidationService(CasterContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> DirectoryExists(Guid directoryId)
    {
        return await _dbContext.Directories.AnyAsync(x => x.Id == directoryId);
    }

    public async Task<bool> DesignExists(Guid designId)
    {
        return await _dbContext.Designs.AnyAsync(x => x.Id == designId);
    }

    public async Task<bool> ModuleExists(Guid moduleId)
    {
        return await _dbContext.Modules.AnyAsync(x => x.Id == moduleId);
    }

    public async Task<bool> ProjectExists(Guid projectId)
    {
        return await _dbContext.Projects.AnyAsync(x => x.Id == projectId);
    }

    public async Task<bool> PartitionExists(Guid partitionId)
    {
        return await _dbContext.Partitions.AnyAsync(x => x.Id == partitionId);
    }

    public async Task<bool> PoolExists(Guid poolId)
    {
        return await _dbContext.Pools.AnyAsync(x => x.Id == poolId);
    }

    public async Task<bool> WorkspaceExists(Guid workspaceId)
    {
        return await _dbContext.Workspaces.AnyAsync(x => x.Id == workspaceId);
    }

    public async Task<bool> VlanExists(Guid vlanId)
    {
        return await _dbContext.Vlans.AnyAsync(x => x.Id == vlanId);
    }

    public async Task<bool> UserExists(Guid userId)
    {
        return await _dbContext.Users.AnyAsync(x => x.Id == userId);
    }

    public async Task<bool> GroupExists(Guid groupId)
    {
        return await _dbContext.Groups.AnyAsync(x => x.Id == groupId);
    }

    public async Task<bool> ProjectRoleExists(Guid roleId)
    {
        return await _dbContext.ProjectRoles.AnyAsync(x => x.Id == roleId);
    }

    public async Task<bool> SystemRoleExists(Guid roleId)
    {
        return await _dbContext.SystemRoles.AnyAsync(x => x.Id == roleId);
    }
}
