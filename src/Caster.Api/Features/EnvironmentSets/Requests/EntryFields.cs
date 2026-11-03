// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Domain.Models;
using Caster.Api.Domain.Services.EnvironmentSets;
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
}

public class EnvironmentSetEntryFieldsValidator<T> : AbstractValidator<T> where T : IEnvironmentSetEntryFields
{
    public const int MaximumNameLength = 255;

    public EnvironmentSetEntryFieldsValidator(ISecretSource secretSource)
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
    }
}
