// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using Caster.Api.Infrastructure.Options;

namespace Caster.Api.Domain.Services.Inventory;

/// <summary>
/// Last line of defence so a provider error string can never carry a configured
/// credential out to an api response or a log line. Shared by every provider so
/// there is exactly one redaction rule to audit.
/// </summary>
public static class InventoryRedaction
{
    public const string Mask = "***";

    /// <summary>
    /// Replaces every configured secret in <paramref name="message"/> with
    /// <see cref="Mask"/>. <paramref name="extraSecrets"/> covers values that only
    /// exist for the duration of a read, such as a Proxmox auth ticket.
    /// </summary>
    public static string Redact(string message, InfrastructureOptions options, params string[] extraSecrets)
    {
        if (string.IsNullOrEmpty(message))
            return message;

        foreach (var secret in Secrets(options, extraSecrets))
        {
            message = message.Replace(secret, Mask);
        }

        return message;
    }

    private static IEnumerable<string> Secrets(InfrastructureOptions options, string[] extraSecrets)
    {
        if (!string.IsNullOrEmpty(options?.Password))
        {
            yield return options.Password;
        }

        if (!string.IsNullOrEmpty(options?.ApiToken))
        {
            // The whole token first, then the secret half on its own, so a message
            // that only echoed the secret is still covered.
            yield return options.ApiToken;

            var separator = options.ApiToken.IndexOf('=');

            if (separator >= 0 && separator < options.ApiToken.Length - 1)
            {
                yield return options.ApiToken[(separator + 1)..];
            }
        }

        if (extraSecrets == null)
            yield break;

        foreach (var secret in extraSecrets)
        {
            if (!string.IsNullOrEmpty(secret))
            {
                yield return secret;
            }
        }
    }
}
