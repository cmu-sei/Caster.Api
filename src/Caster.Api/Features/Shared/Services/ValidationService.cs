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
}
