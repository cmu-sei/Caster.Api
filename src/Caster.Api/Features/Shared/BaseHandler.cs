// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;
using Caster.Api.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Caster.Api.Infrastructure.Identity;

namespace Caster.Api.Features.Shared;

public interface IDependencyAggregate<T>
{
    CasterContext DbContext { get; }
    IMapper Mapper { get; }
    IAuthorizationService AuthorizationService { get; }
    IIdentityResolver IdentityResolver { get; }
    ILogger<T> Logger { get; }
}

public class DependencyAggregate<T> : IDependencyAggregate<T>
{
    public DependencyAggregate(
        CasterContext dbContext,
        IMapper mapper,
        IAuthorizationService authorizationService,
        IIdentityResolver identityResolver,
        ILogger<T> logger)
    {
        DbContext = dbContext;
        Mapper = mapper;
        AuthorizationService = authorizationService;
        IdentityResolver = identityResolver;
        Logger = logger;
    }

    public CasterContext DbContext { get; }
    public IMapper Mapper { get; }
    public IAuthorizationService AuthorizationService { get; }
    public IIdentityResolver IdentityResolver { get; }
    public ILogger<T> Logger { get; }
}

public class BaseHandler<T>
{
    protected readonly CasterContext _db;
    protected readonly IMapper _mapper;
    protected readonly IAuthorizationService _authorizationService;
    protected readonly ClaimsPrincipal _user;
    protected readonly ILogger<T> _logger;

    public BaseHandler(IDependencyAggregate<T> aggregate)
    {
        _db = aggregate.DbContext;
        _mapper = aggregate.Mapper;
        _authorizationService = aggregate.AuthorizationService;
        _user = aggregate.IdentityResolver.GetClaimsPrincipal();
        _logger = aggregate.Logger;
    }
}
