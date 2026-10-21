// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Domain.Models;

/// <summary>
/// The hypervisors Caster can build on, told apart by the Terraform provider a Module
/// declares and by the provider credentials the server passes to Terraform.
/// </summary>
public static class Hypervisors
{
    public const string Proxmox = "Proxmox";
    public const string VSphere = "vSphere";

    public static readonly string[] All = [Proxmox, VSphere];

    // provider type (the last part of a source address such as bpg/proxmox) -> hypervisor
    private static readonly Dictionary<string, string> ByProviderType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["proxmox"] = Proxmox,
        ["vsphere"] = VSphere,
    };

    // the environment variable each provider needs set before it can reach its hypervisor
    private static readonly (string Hypervisor, string Variable)[] Endpoints =
    [
        (Proxmox, "PROXMOX_VE_ENDPOINT"),
        (VSphere, "VSPHERE_SERVER"),
    ];

    /// <summary>
    /// The hypervisor a Module builds on, from its provider sources. Null when it uses none
    /// of them (a Module that works anywhere) or when its providers were never read.
    /// </summary>
    public static string ForProviders(IEnumerable<string> providerSources)
    {
        var found = (providerSources ?? [])
            .Select(s => s?.Split('/').Last())
            .Where(t => t != null && ByProviderType.ContainsKey(t))
            .Select(t => ByProviderType[t])
            .Distinct()
            .ToList();

        // a Module that spans two hypervisors belongs to neither alone
        return found.Count == 1 ? found[0] : null;
    }

    /// <summary>
    /// The hypervisors Terraform runs have an endpoint for, in <see cref="All"/> order,
    /// resolved as runs resolve their environment: a Terraform:EnvironmentVariables:Direct
    /// value wins, otherwise Caster's own value when InheritAll is on or an Inherit pattern
    /// (a regular expression) matches the name.
    /// </summary>
    public static string[] Configured(TerraformOptions options, Func<string, string> environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        var env = options?.EnvironmentVariables ?? new EnvironmentVariableOptions();
        var inherit = (env.Inherit ?? []).Select(p => new Regex(p)).ToArray();

        string Value(string variable)
        {
            if (env.Direct != null && env.Direct.TryGetValue(variable, out var direct))
                return direct;
            return env.InheritAll || inherit.Any(r => r.IsMatch(variable)) ? environment(variable) : null;
        }

        return Endpoints
            .Where(e => !string.IsNullOrWhiteSpace(Value(e.Variable)))
            .Select(e => e.Hypervisor)
            .ToArray();
    }
}
