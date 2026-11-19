// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
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

public class Edit
{
    [DataContract(Name = "EditModuleScopeCommand")]
    public class Command : IRequest<ModuleScope>
    {
        [JsonIgnore]
        public Guid Id { get; set; }

        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public string Description { get; set; }

        /// <summary>Module sources, or folders of them: <c>afp</c> or <c>afp/exercise-x</c>. At least one.</summary>
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

    /// <summary>Changes take effect on the next request of every project the scope reaches.</summary>
    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext) : BaseHandler<Command, ModuleScope>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageModules], cancellationToken);

        public override async Task<ModuleScope> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var scope = await dbContext.ModuleScopes.SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
                ?? throw new EntityNotFoundException<Domain.Models.ModuleScope>();

            var name = request.Name.Trim();
            await dbContext.EnsureUniqueScopeName(name, scope.Id, cancellationToken);

            scope.Name = name;
            scope.Description = request.Description;
            scope.Paths = ModuleScopeFields.Clean(request.Paths);
            await dbContext.SaveChangesAsync(cancellationToken);

            return await dbContext.GetScope(scope.Id, cancellationToken);
        }
    }
}
