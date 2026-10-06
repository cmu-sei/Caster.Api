// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using MediatR.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Caster.Api.Features.Shared.Behaviors;

public class ValidationBehavior<TRequest> : IRequestPreProcessor<TRequest>
{
    // MS.DI resolves closed generics exactly, so validators declared over an
    // interface the request implements must be looked up by that interface
    private static readonly Type[] _validatorTypes = typeof(TRequest).GetInterfaces()
        .Prepend(typeof(TRequest))
        .Select(t => typeof(IValidator<>).MakeGenericType(t))
        .ToArray();

    private readonly IServiceProvider _services;

    public ValidationBehavior(IServiceProvider services)
      => _services = services;

    public async Task Process(TRequest request, CancellationToken cancellationToken)
    {
        // Invoke the validators
        var failures = new List<ValidationFailure>();

        foreach (var validator in _validatorTypes.SelectMany(t => _services.GetServices(t)).Cast<IValidator>())
        {
            var result = await validator.ValidateAsync(new ValidationContext<TRequest>(request), cancellationToken);

            if (!result.IsValid)
            {
                failures.AddRange(result.Errors);
            }
        }

        if (failures.Any())
        {
            // Map the validation failures and throw an error,
            // this stops the execution of the request
            var errors = failures
                .GroupBy(x => x.PropertyName)
                .ToDictionary(k => k.Key, v => v.Select(x => x.ErrorMessage).ToArray());
            throw new Infrastructure.Exceptions.ValidationException(errors);
        }
    }
}

