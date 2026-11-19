// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Caster.Api.Data;
using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.EnvironmentSets;
using Caster.Api.Domain.Services.Inventory;
using Caster.Api.Features.Shared;
using Caster.Api.Infrastructure.Authorization;
using Caster.Api.Infrastructure.Exceptions;
using FluentValidation;
using MediatR;

namespace Caster.Api.Features.EnvironmentSets;

/// <summary>A connection's fields and presets, for building connection entries. Never values.</summary>
public class EnvironmentSetConnection
{
    public string Name { get; set; }
    public string Provider { get; set; }
    public EnvironmentSetConnectionField[] Fields { get; set; } = [];
    public EnvironmentSetConnectionPreset[] Presets { get; set; } = [];
}

public class EnvironmentSetConnectionField
{
    public ConnectionField Field { get; set; }
    public bool IsSecret { get; set; }
    public bool IsAvailable { get; set; }

    /// <summary>Why the connection has no value for the field, when it hasn't.</summary>
    public string Unavailable { get; set; }
}

public class EnvironmentSetConnectionPreset
{
    /// <summary>The value to pass as the preset of <c>from-connection</c>, e.g. "bpg/proxmox".</summary>
    public string Id { get; set; }

    public string Name { get; set; }

    /// <summary>The entries the preset would make for this connection.</summary>
    public EnvironmentSetConnectionPresetEntry[] Entries { get; set; } = [];

    /// <summary>Whether the connection has every field the preset uses.</summary>
    public bool IsAvailable { get; set; }
}

public class EnvironmentSetConnectionPresetEntry
{
    public string TargetName { get; set; }
    public ConnectionField Field { get; set; }
}

public class GetConnections
{
    [DataContract(Name = "GetEnvironmentSetConnectionsQuery")]
    public class Query : IRequest<EnvironmentSetConnection[]>
    {
    }

    public class Handler(
        ICasterAuthorizationService authorizationService,
        IInfrastructureConnectionCatalog connections) : BaseHandler<Query, EnvironmentSetConnection[]>
    {
        public override async Task<bool> Authorize(Query request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageEnvironmentSets], cancellationToken);

        public override Task<EnvironmentSetConnection[]> HandleRequest(Query request, CancellationToken cancellationToken) =>
            Task.FromResult(connections.GetAll().Select(ToView).ToArray());

        private static EnvironmentSetConnection ToView(InfrastructureConnection connection)
        {
            var fields = ConnectionFields.For(connection.Options.Provider)
                .Select(field =>
                {
                    var value = ConnectionFields.Resolve(connection.Options, field);
                    return new EnvironmentSetConnectionField
                    {
                        Field = field,
                        IsSecret = ConnectionFields.IsSecret(field),
                        IsAvailable = value.IsAvailable,
                        Unavailable = value.Unavailable
                    };
                })
                .ToArray();

            return new EnvironmentSetConnection
            {
                Name = connection.Name,
                Provider = connection.Options.Provider,
                Fields = fields,
                Presets = ConnectionPresets.For(connection.Options.Provider)
                    .Select(preset =>
                    {
                        var entries = preset.Entries(connection.Options);
                        return new EnvironmentSetConnectionPreset
                        {
                            Id = preset.Id,
                            Name = preset.Name,
                            Entries = entries.Select(x => new EnvironmentSetConnectionPresetEntry { TargetName = x.TargetName, Field = x.Field }).ToArray(),
                            IsAvailable = entries.All(x => fields.Any(f => f.Field == x.Field && f.IsAvailable))
                        };
                    })
                    .ToArray()
            };
        }
    }
}

/// <summary>Creates a set holding a provider preset's entries for one connection.</summary>
public class CreateFromConnection
{
    [DataContract(Name = "CreateEnvironmentSetFromConnectionCommand")]
    public class Command : IRequest<EnvironmentSet>
    {
        /// <summary>The infrastructure connection, by name.</summary>
        [DataMember]
        public string ConnectionName { get; set; }

        /// <summary>The preset, e.g. "bpg/proxmox". See <c>GET environment-sets/connections</c>.</summary>
        [DataMember]
        public string Preset { get; set; }

        /// <summary>The set's name. Without one it is the connection's name and the preset's.</summary>
        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public string Description { get; set; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.ConnectionName).NotEmpty();
            RuleFor(x => x.Preset).NotEmpty();
            RuleFor(x => x.Name).MaximumLength(EnvironmentSetEntryFieldsValidator<IEnvironmentSetEntryFields>.MaximumNameLength);
        }
    }

    public class Handler(
        ICasterAuthorizationService authorizationService,
        CasterContext dbContext,
        IInfrastructureConnectionCatalog connections,
        IEnvironmentSetViews views) : BaseHandler<Command, EnvironmentSet>
    {
        public override async Task<bool> Authorize(Command request, CancellationToken cancellationToken) =>
            await authorizationService.Authorize([SystemPermission.ManageEnvironmentSets], cancellationToken) &&
            await authorizationService.Authorize([SystemPermission.ManageInfrastructure], cancellationToken);

        public override async Task<EnvironmentSet> HandleRequest(Command request, CancellationToken cancellationToken)
        {
            var connection = connections.Find(request.ConnectionName)
                ?? throw new EntityNotFoundException<EnvironmentSetConnection>($"There is no infrastructure connection named \"{request.ConnectionName}\".");

            var preset = ConnectionPresets.Find(request.Preset);

            if (preset is null || !string.Equals(preset.Provider, connection.Options.Provider?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new Infrastructure.Exceptions.ValidationException(new()
                {
                    [nameof(Command.Preset)] = [$"\"{request.Preset}\" is not a preset for {connection.Options.Provider} connections."]
                });
            }

            var name = string.IsNullOrWhiteSpace(request.Name) ? $"{connection.Name} ({preset.Name})" : request.Name.Trim();
            await dbContext.EnsureUniqueSetName(name, null, cancellationToken);

            var set = new Domain.Models.EnvironmentSet { Name = name, Description = request.Description };

            foreach (var entry in preset.Entries(connection.Options))
            {
                set.Entries.Add(new Domain.Models.EnvironmentSetEntry
                {
                    Kind = EnvironmentSetEntryKind.EnvironmentVariable,
                    TargetName = entry.TargetName,
                    ValueSource = EnvironmentSetValueSource.Connection,
                    ConnectionName = connection.Name,
                    ConnectionField = entry.Field
                });
            }

            dbContext.EnvironmentSets.Add(set);
            await dbContext.SaveChangesAsync(cancellationToken);

            return await views.GetSet(set.Id, cancellationToken);
        }
    }
}
