// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.ModuleScopes;

public class Create
{
    [DataContract(Name = "CreateModuleScopeCommand")]
    public class Command : IRequest<ModuleScope>
    {
        /// <summary>The name of the scope. Unique.</summary>
        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public string Description { get; set; }

        /// <summary>Module sources, or folders of them: <c>cmu</c> or <c>cmu/exercise-x</c>. At least one.</summary>
        [DataMember]
        public List<string> Paths { get; set; } = [];
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name).ValidName();
            RuleFor(x => x.Paths).ValidPaths();
        }
    }

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Command, ModuleScope>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageModules], cancellationToken);

        public override async Task<ModuleScope> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var name = request.Name.Trim();
            await dbContext.EnsureUniqueScopeName(name, null, cancellationToken);

            var scope = new Domain.Models.ModuleScope
            {
                Name = name,
                Description = request.Description,
                Paths = ModuleScopeFields.Clean(request.Paths)
            };

            dbContext.ModuleScopes.Add(scope);
            await dbContext.SaveChangesAsync(cancellationToken);

            return await dbContext.GetScope(scope.Id, cancellationToken);
        }
    }
}

internal static class ModuleScopeNames
{
    public static async Task EnsureUniqueScopeName(this CasterContext dbContext, string name, System.Guid? exceptId, CancellationToken cancellationToken)
    {
        var lowered = name.ToLower();

        if (await dbContext.ModuleScopes.AnyAsync(x => x.Name.ToLower() == lowered && x.Id != exceptId, cancellationToken))
        {
            throw new ConflictException($"A Module Scope named \"{name}\" already exists.");
        }
    }
}
