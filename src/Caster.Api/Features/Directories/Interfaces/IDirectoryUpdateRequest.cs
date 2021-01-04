// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Domain.Services;
using FluentValidation;

namespace Caster.Api.Features.Directories.Interfaces
{
    public interface IDirectoryUpdateRequest
    {
        string TerraformVersion { get; set; }
    }

    public class IWorkspaceUpdateValidator : AbstractValidator<IDirectoryUpdateRequest>
    {
        public IWorkspaceUpdateValidator(ITerraformService terraformService)
        {
            RuleFor(x => x.TerraformVersion)
                .Must(x => terraformService.IsValidVersion(x))
                .WithMessage("The specified version is not available. Please contact a system administrator to request it be added to the system.")
                .When(x => !string.IsNullOrEmpty(x.TerraformVersion));
        }
    }
}
