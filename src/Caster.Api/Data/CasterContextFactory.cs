// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Data;

public class CasterContextFactory : IDbContextFactory<CasterContext>
{
    private readonly IDbContextFactory<CasterContext> _pooledFactory;
    private readonly IServiceProvider _serviceProvider;

    public CasterContextFactory(
        IDbContextFactory<CasterContext> pooledFactory,
        IServiceProvider serviceProvider)
    {
        _pooledFactory = pooledFactory;
        _serviceProvider = serviceProvider;
    }

    public CasterContext CreateDbContext()
    {
        var context = _pooledFactory.CreateDbContext();

        // Inject the current scope's ServiceProvider
        context.ServiceProvider = _serviceProvider;
        return context;
    }
}