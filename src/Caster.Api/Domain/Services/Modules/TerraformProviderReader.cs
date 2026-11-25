// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Caster.Api.Domain.Services.Modules;

/// <summary>
/// Reads the provider source addresses (such as <c>bpg/proxmox</c>) a Module root
/// declares under <c>terraform { required_providers { ... } }</c>, from its <c>.tf</c>
/// and <c>.tf.json</c> files. Only the root's own files are read, not nested modules.
/// </summary>
public static class TerraformProviderReader
{
    private static readonly Regex RequiredProviders = new(@"\brequired_providers\s*\{");
    private static readonly Regex Source = new(@"\bsource\s*=\s*""([^""]+)""");

    /// <summary>
    /// Lower-cased, distinct and sorted. Empty when the root declares none, as a Module
    /// that only uses built-in or implicit providers does.
    /// </summary>
    public static string[] Read(string directory)
    {
        var sources = new SortedSet<string>(StringComparer.Ordinal);
        if (!Directory.Exists(directory))
            return [];

        foreach (var path in Directory.EnumerateFiles(directory))
        {
            try
            {
                if (path.EndsWith(".tf.json", StringComparison.OrdinalIgnoreCase))
                    ReadJson(File.ReadAllBytes(path), sources);
                else if (path.EndsWith(".tf", StringComparison.OrdinalIgnoreCase))
                    ReadHcl(File.ReadAllText(path), sources);
            }
            catch (JsonException)
            {
                // A file that does not parse declares nothing we can use; the variables
                // and outputs files report their own errors.
            }
        }

        return sources.ToArray();
    }

    public static void ReadHcl(string text, ISet<string> sources)
    {
        text = StripComments(text);
        foreach (Match match in RequiredProviders.Matches(text))
        {
            var block = BalancedBlock(text, match.Index + match.Length - 1);
            foreach (Match source in Source.Matches(block))
                sources.Add(source.Groups[1].Value.Trim().ToLowerInvariant());
        }
    }

    public static void ReadJson(byte[] json, ISet<string> sources)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("terraform", out var terraform))
            return;

        // "terraform" may be one block or a list of them, and so may "required_providers".
        foreach (var block in Blocks(terraform))
        {
            if (!block.TryGetProperty("required_providers", out var required))
                continue;

            foreach (var providers in Blocks(required))
            {
                foreach (var provider in providers.EnumerateObject())
                {
                    if (provider.Value.ValueKind == JsonValueKind.Object &&
                        provider.Value.TryGetProperty("source", out var source) &&
                        source.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(source.GetString()))
                    {
                        sources.Add(source.GetString().Trim().ToLowerInvariant());
                    }
                }
            }
        }
    }

    private static IEnumerable<JsonElement> Blocks(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => [element],
            JsonValueKind.Array => element.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object),
            _ => []
        };

    // Drops #, // and /* */ comments, leaving strings (which may hold // in a URL) alone.
    private static string StripComments(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        var inString = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < text.Length)
                    sb.Append(text[++i]);
                else if (c == '"')
                    inString = false;
            }
            else if (c == '"')
            {
                inString = true;
                sb.Append(c);
            }
            else if (c == '#' || (c == '/' && i + 1 < text.Length && text[i + 1] == '/'))
            {
                while (i < text.Length && text[i] != '\n')
                    i++;
                sb.Append('\n');
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 1;
                sb.Append(' ');
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    // The text between the brace at openIndex and its match, ignoring braces in strings.
    private static string BalancedBlock(string text, int openIndex)
    {
        var depth = 0;
        var inString = false;
        for (var i = openIndex; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\')
                    i++;
                else if (c == '"')
                    inString = false;
                continue;
            }

            if (c == '"')
                inString = true;
            else if (c == '{')
                depth++;
            else if (c == '}' && --depth == 0)
                return text[(openIndex + 1)..i];
        }
        return text[(openIndex + 1)..];
    }
}
