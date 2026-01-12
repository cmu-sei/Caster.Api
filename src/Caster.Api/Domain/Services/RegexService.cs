// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Caster.Api.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Caster.Api.Domain.Services;

public interface IRegexService
{
    Regex[] GetEnvironmentVariableRegexes();
}

public class RegexService : IRegexService
{
    private Regex[] _environmentVarRegexes = [];

    public RegexService(IOptionsMonitor<TerraformOptions> monitor)
    {
        this.BuildRegexes(monitor.CurrentValue.EnvironmentVariables);

        monitor.OnChange(x =>
        {
            this.BuildRegexes(x.EnvironmentVariables);
        });
    }

    private void BuildRegexes(EnvironmentVariableOptions options)
    {
        var regexes = new List<Regex>();
        foreach (var regex in options.Inherit)
        {
            regexes.Add(new Regex(regex, RegexOptions.Compiled));
        }
        _environmentVarRegexes = regexes.ToArray();
    }

    public Regex[] GetEnvironmentVariableRegexes()
    {
        return _environmentVarRegexes.ToArray();
    }
}
