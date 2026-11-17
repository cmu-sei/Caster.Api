// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.EnvironmentSets;
using Caster.Api.Domain.Services.Inventory;
using FluentValidation;

namespace Caster.Api.Features.EnvironmentSets;

/// <summary>The fields of an Environment Set entry a create or edit sends.</summary>
public interface IEnvironmentSetEntryFields
{
    EnvironmentSetEntryKind Kind { get; }
    string TargetName { get; }
    EnvironmentSetValueSource ValueSource { get; }
    string SecretName { get; }
    string Value { get; }
    string ConnectionName { get; }
    ConnectionField? ConnectionField { get; }
}

public class EnvironmentSetEntryFieldsValidator<T> : AbstractValidator<T> where T : IEnvironmentSetEntryFields
{
    public const int MaximumNameLength = 255;

    public EnvironmentSetEntryFieldsValidator(ISecretSource secretSource, IInfrastructureConnectionCatalog connections)
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.ValueSource).IsInEnum();

        RuleFor(x => x.TargetName)
            .MaximumLength(MaximumNameLength)
            .Custom((name, context) =>
            {
                var error = EnvironmentSetRules.GetTargetNameError(context.InstanceToValidate.Kind, name);

                if (error is not null)
                {
                    context.AddFailure(nameof(IEnvironmentSetEntryFields.TargetName), error);
                }
            });

        When(x => x.ValueSource == EnvironmentSetValueSource.Secret, () =>
        {
            RuleFor(x => x.SecretName)
                .NotEmpty()
                .WithMessage("A secret entry needs the name of the API environment variable to read.")
                .MaximumLength(MaximumNameLength)
                .Must(x => string.IsNullOrEmpty(x) || secretSource.IsAllowed(x))
                .WithMessage("The secret source must be an API environment variable whose name matches one of the secret patterns (Terraform:Secrets:Patterns).");

            RuleFor(x => x.Value)
                .Empty()
                .WithMessage("A secret entry cannot store a value; its value is read from the API environment at run time.");
        });

        When(x => x.ValueSource == EnvironmentSetValueSource.Plain, () =>
        {
            RuleFor(x => x.Value)
                .NotNull()
                .WithMessage("A plain entry needs a value.");

            RuleFor(x => x.SecretName)
                .Empty()
                .WithMessage("A plain entry cannot name a secret source.");
        });

        When(x => x.ValueSource != EnvironmentSetValueSource.Connection, () =>
        {
            RuleFor(x => x.ConnectionName)
                .Empty()
                .WithMessage("Only a connection entry names a connection.");

            RuleFor(x => x.ConnectionField)
                .Null()
                .WithMessage("Only a connection entry names a connection field.");
        });

        When(x => x.ValueSource == EnvironmentSetValueSource.Connection, () =>
        {
            RuleFor(x => x.SecretName)
                .Empty()
                .WithMessage("A connection entry cannot name a secret source.");

            RuleFor(x => x.Value)
                .Empty()
                .WithMessage("A connection entry cannot store a value; its value is read from the connection at run time.");

            RuleFor(x => x.ConnectionField)
                .NotNull()
                .WithMessage("A connection entry needs the connection field to read.")
                .IsInEnum();

            RuleFor(x => x.ConnectionName)
                .NotEmpty()
                .WithMessage("A connection entry needs the name of an infrastructure connection.")
                .MaximumLength(MaximumNameLength)
                .Custom((name, context) =>
                {
                    if (string.IsNullOrWhiteSpace(name))
                        return;

                    var connection = connections.Find(name);

                    if (connection is null)
                    {
                        context.AddFailure(nameof(IEnvironmentSetEntryFields.ConnectionName),
                            $"There is no infrastructure connection named \"{name.Trim()}\" in Infrastructure:Connections.");
                        return;
                    }

                    if (context.InstanceToValidate.ConnectionField is { } field && !ConnectionFields.AppliesTo(field, connection.Options.Provider))
                    {
                        context.AddFailure(nameof(IEnvironmentSetEntryFields.ConnectionField),
                            $"{field} doesn't apply to {connection.Options.Provider} connections.");
                    }
                });
        });

        // Terraform writes variable values into saved plans and state.
        RuleFor(x => x.Kind)
            .Must((fields, kind) => kind != EnvironmentSetEntryKind.TerraformVariable || !IsSecret(fields))
            .WithMessage("A secret can't be a Terraform variable: Terraform saves variables in plans and state. Use an environment variable, as provider credentials are.");
    }

    /// <summary>Whether the entry's value is a secret: a Secret entry, or a connection credential.</summary>
    public static bool IsSecret(IEnvironmentSetEntryFields fields) => fields.ValueSource switch
    {
        EnvironmentSetValueSource.Secret => true,
        EnvironmentSetValueSource.Connection => fields.ConnectionField is { } field && ConnectionFields.IsSecret(field),
        _ => false
    };
}
