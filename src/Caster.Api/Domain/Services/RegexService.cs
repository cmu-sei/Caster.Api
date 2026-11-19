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
    /// <summary>The <c>Terraform:EnvironmentVariables:Inherit</c> patterns.</summary>
    Regex[] GetEnvironmentVariableRegexes();

    /// <summary>The <c>Terraform:Secrets:Patterns</c> patterns.</summary>
    Regex[] GetSecretRegexes();

    /// <summary>True if <paramref name="name"/> matches one of the secret patterns.</summary>
    bool IsSecretName(string name);
}

public class RegexService : IRegexService
{
    private Regex[] _environmentVarRegexes = [];
    private Regex[] _secretRegexes = [];

    public RegexService(IOptionsMonitor<TerraformOptions> monitor)
    {
        this.BuildRegexes(monitor.CurrentValue);

        monitor.OnChange(x =>
        {
            this.BuildRegexes(x);
        });
    }

    private void BuildRegexes(TerraformOptions options)
    {
        _environmentVarRegexes = Build(options.EnvironmentVariables?.Inherit, skipEmpty: false);

        // An empty secret pattern would match every API variable, the API's own
        // credentials included, and let an Environment Set hand them to a project.
        // A blank entry (an unset Helm value, say) must not mean that.
        _secretRegexes = Build(options.Secrets?.Patterns, skipEmpty: true);
    }

    private static Regex[] Build(IEnumerable<string> patterns, bool skipEmpty)
    {
        var regexes = new List<Regex>();

        foreach (var pattern in patterns ?? [])
        {
            if (pattern is null || (skipEmpty && string.IsNullOrWhiteSpace(pattern)))
            {
                continue;
            }

            regexes.Add(new Regex(pattern, RegexOptions.Compiled));
        }

        return regexes.ToArray();
    }

    public Regex[] GetEnvironmentVariableRegexes()
    {
        return _environmentVarRegexes.ToArray();
    }

    public Regex[] GetSecretRegexes()
    {
        return _secretRegexes.ToArray();
    }

    public bool IsSecretName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        return _secretRegexes.Any(x => x.IsMatch(name));
    }
}
