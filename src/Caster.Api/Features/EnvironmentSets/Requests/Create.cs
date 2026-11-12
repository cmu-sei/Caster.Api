// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

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

namespace Caster.Api.Features.EnvironmentSets;

public class Create
{
    [DataContract(Name = "CreateEnvironmentSetCommand")]
    public class Command : IRequest<EnvironmentSet>
    {
        /// <summary>The name of the set. Unique.</summary>
        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public string Description { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(EnvironmentSetEntryFieldsValidator<IEnvironmentSetEntryFields>.MaximumNameLength);
        }
    }

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext, IEnvironmentSetViews views) : BaseHandler<Command, EnvironmentSet>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageEnvironmentSets], cancellationToken);

        public override async Task<EnvironmentSet> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var name = request.Name.Trim();
            await dbContext.EnsureUniqueSetName(name, null, cancellationToken);

            var set = new Domain.Models.EnvironmentSet
            {
                Name = name,
                Description = request.Description
            };

            dbContext.EnvironmentSets.Add(set);
            await dbContext.SaveChangesAsync(cancellationToken);

            return await views.GetSet(set.Id, cancellationToken);
        }
    }
}

internal static class EnvironmentSetNames
{
    public static async Task EnsureUniqueSetName(this CasterContext dbContext, string name, System.Guid? exceptId, CancellationToken cancellationToken)
    {
        var lowered = name.ToLower();

        if (await dbContext.EnvironmentSets.AnyAsync(x => x.Name.ToLower() == lowered && x.Id != exceptId, cancellationToken))
        {
            throw new ConflictException($"An Environment Set named \"{name}\" already exists.");
        }
    }
}
