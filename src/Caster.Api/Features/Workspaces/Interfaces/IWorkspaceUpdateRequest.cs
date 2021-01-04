// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using Caster.Api.Domain.Services;
using FluentValidation;

namespace Caster.Api.Features.Workspaces.Interfaces
{
    public interface IWorkspaceUpdateRequest
    {
        string Name { get; set; }
        string TerraformVersion { get; set; }
    }

    public class IWorkspaceUpdateValidator : AbstractValidator<IWorkspaceUpdateRequest>
    {
        public IWorkspaceUpdateValidator(ITerraformService terraformService)
        {
            RuleFor(x => x.Name)
                .MinimumLength(1)
                .MaximumLength(90)
                .Must(x => x.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.'))
                .WithMessage($"Workspace names need to be 90 characters or less and can only include letters, numbers, -, _, and .")
                .When(x => x.Name != null);

            RuleFor(x => x.Name)
                .NotNull()
                .When(x => !(x is PartialEdit.Command));

            RuleFor(x => x.TerraformVersion)
                .Must(x => terraformService.IsValidVersion(x))
                .WithMessage("The specified version is not available. Please contact a system administrator to request it be added to the system.")
                .When(x => !string.IsNullOrEmpty(x.TerraformVersion));
        }
    }
}
