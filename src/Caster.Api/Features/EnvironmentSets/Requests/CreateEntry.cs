// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.EnvironmentSets;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Caster.Api.Features.EnvironmentSets;

public class CreateEntry
{
    [DataContract(Name = "CreateEnvironmentSetEntryCommand")]
    public class Command : IRequest<EnvironmentSet>, IEnvironmentSetEntryFields
    {
        [JsonIgnore]
        public Guid EnvironmentSetId { get; set; }

        /// <summary>An environment variable, or a Terraform variable (injected as TF_VAR_&lt;TargetName&gt;).</summary>
        [DataMember]
        public EnvironmentSetEntryKind Kind { get; set; }

        /// <summary>The name inside the run. For a Terraform variable, without TF_VAR_.</summary>
        [DataMember]
        public string TargetName { get; set; }

        /// <summary>Plain (a stored, non-secret value) or Secret (read from an API environment variable at run time).</summary>
        [DataMember]
        public EnvironmentSetValueSource ValueSource { get; set; }

        /// <summary>For a secret: the API environment variable to read. Must match a secret pattern.</summary>
        [DataMember]
        public string SecretName { get; set; }

        /// <summary>For a plain entry: the value. Visible to members of every project the set is assigned to.</summary>
        [DataMember]
        public string Value { get; set; }
    }

    public class Validator(ISecretSource secretSource) : EnvironmentSetEntryFieldsValidator<Command>(secretSource);

    public class Handler(ICasterAuthorizationService authorizationService, CasterContext dbContext, IEnvironmentSetViews views) : BaseHandler<Command, EnvironmentSet>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageEnvironmentSets], cancellationToken);

        public override async Task<EnvironmentSet> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var set = await dbContext.EnvironmentSets
                .Include(x => x.Entries)
                .SingleOrDefaultAsync(x => x.Id == request.EnvironmentSetId, cancellationToken)
                ?? throw new EntityNotFoundException<EnvironmentSet>();

            var entry = new Domain.Models.EnvironmentSetEntry { EnvironmentSetId = set.Id };
            entry.Apply(request);

            await dbContext.EnsureNoConflicts(set.Id, [.. set.Entries, entry], null, cancellationToken);

            dbContext.EnvironmentSetEntries.Add(entry);
            await dbContext.SaveChangesAsync(cancellationToken);

            return await views.GetSet(set.Id, cancellationToken);
        }
    }
}

internal static class EnvironmentSetEntryExtensions
{
    public static void Apply(this Domain.Models.EnvironmentSetEntry entry, IEnvironmentSetEntryFields fields)
    {
        entry.Kind = fields.Kind;
        entry.TargetName = fields.TargetName;
        entry.ValueSource = fields.ValueSource;
        entry.SecretName = fields.ValueSource == EnvironmentSetValueSource.Secret ? fields.SecretName : null;
        entry.Value = fields.ValueSource == EnvironmentSetValueSource.Plain ? fields.Value : null;
    }
}
