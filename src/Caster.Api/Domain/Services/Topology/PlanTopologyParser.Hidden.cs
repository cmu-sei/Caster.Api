// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Caster.Api.Domain.Services.Topology;

/// <summary>
/// What a plan or state marks as not known until apply, or as sensitive.
/// Nothing under such a path becomes a label or a property.
/// </summary>
public static partial class PlanTopologyParser
{
    private const char Separator = '\u001f';

    private static string Join(string path, string name) => path + Separator + name;

    private static string Join(string path, int index) => Join(path, index.ToString(CultureInfo.InvariantCulture));

    #region Unknown and sensitive values

    /// <summary>
    /// The paths an <c>after_unknown</c>, <c>after_sensitive</c> or
    /// <c>sensitive_values</c> mask marks: it has the shape of the values,
    /// with <c>true</c> where one is unknown or sensitive.
    /// </summary>
    private static List<string> Mask(JsonElement element, string name)
    {
        var paths = new List<string>();

        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var mask))
        {
            Flatten(mask, string.Empty, paths);
        }

        return paths;
    }

    private static void Flatten(JsonElement mask, string path, List<string> paths)
    {
        switch (mask.ValueKind)
        {
            case JsonValueKind.True:
                paths.Add(path);
                break;
            case JsonValueKind.Object:
                foreach (var child in mask.EnumerateObject())
                {
                    Flatten(child.Value, Join(path, child.Name), paths);
                }

                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var child in mask.EnumerateArray())
                {
                    Flatten(child, Join(path, i++), paths);
                }

                break;
        }
    }

    /// <summary>
    /// A state file instance's <c>sensitive_attributes</c>: a list of paths,
    /// each a list of <c>get_attr</c> and <c>index</c> steps. A step that
    /// cannot be read hides everything under the steps before it.
    /// </summary>
    private static List<string> SensitiveAttributes(JsonElement instance)
    {
        var paths = new List<string>();

        if (instance.ValueKind != JsonValueKind.Object || !instance.TryGetProperty("sensitive_attributes", out var attributes))
        {
            return paths;
        }

        if (attributes.ValueKind != JsonValueKind.Array)
        {
            Flatten(attributes, string.Empty, paths);
            return paths;
        }

        foreach (var steps in attributes.EnumerateArray())
        {
            if (steps.ValueKind != JsonValueKind.Array)
            {
                // Not the list of steps this reads: hide the lot rather than guess.
                paths.Add(string.Empty);
                continue;
            }

            var path = string.Empty;

            foreach (var step in steps.EnumerateArray())
            {
                var name = GetString(step, "type") switch
                {
                    "get_attr" => GetString(step, "value"),
                    "index" when step.TryGetProperty("value", out var key) && key.ValueKind == JsonValueKind.Object &&
                        key.TryGetProperty("value", out var index) => index.ValueKind switch
                        {
                            JsonValueKind.String => index.GetString(),
                            JsonValueKind.Number => index.GetRawText(),
                            _ => null,
                        },
                    _ => null,
                };

                if (name == null)
                {
                    break;
                }

                path = Join(path, name);
            }

            paths.Add(path);
        }

        return paths;
    }

    /// <summary>A place in an instance's values that knows whether it is hidden.</summary>
    private readonly record struct Field(JsonElement Element, string Path, HashSet<string> Hidden)
    {
        public static Field Of(Instance instance) => new(instance.Values, string.Empty, instance.Hidden ?? []);

        private bool IsHidden
        {
            get
            {
                var path = Path;

                return Hidden.Contains(path) || Hidden.Any(x => path.StartsWith(x + Separator, StringComparison.Ordinal));
            }
        }

        public Field this[string name] => new(
            Element.ValueKind == JsonValueKind.Object && Element.TryGetProperty(name, out var child) ? child : default,
            Join(Path, name),
            Hidden);

        /// <summary>Follows a dotted path such as <c>cpu.0.cores</c>, as <see cref="GetScalar"/> does.</summary>
        public Field At(string path)
        {
            var field = this;

            foreach (var part in path.Split('.'))
            {
                field = field.Element.ValueKind == JsonValueKind.Array &&
                    int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var i)
                    ? new(i < field.Element.GetArrayLength() ? field.Element[i] : default, Join(field.Path, i), field.Hidden)
                    : field[part];
            }

            return field;
        }

        /// <summary>A block: the first of a list, as the SDK providers write one, or an object, as the framework ones do.</summary>
        public Field One => Element.ValueKind == JsonValueKind.Array
            ? new(Element.GetArrayLength() > 0 ? Element[0] : default, Join(Path, 0), Hidden)
            : this;

        /// <summary>Repeated blocks: a list, or a map keyed by slot (<c>net0</c>, <c>net1</c>) in slot order.</summary>
        public IEnumerable<Field> Items
        {
            get
            {
                var self = this;

                return Element.ValueKind switch
                {
                    JsonValueKind.Array => Element.EnumerateArray().Select((x, i) => new Field(x, Join(self.Path, i), self.Hidden)),
                    JsonValueKind.Object => Element.EnumerateObject()
                        .OrderBy(x => x.Name.Length)
                        .ThenBy(x => x.Name, StringComparer.Ordinal)
                        .Select(x => new Field(x.Value, Join(self.Path, x.Name), self.Hidden)),
                    _ => [],
                };
            }
        }

        /// <summary>The string or number here, or null when there is none or it is hidden.</summary>
        public string Text => IsHidden
            ? null
            : Element.ValueKind switch
            {
                JsonValueKind.String => Element.GetString(),
                JsonValueKind.Number => Element.GetRawText(),
                _ => null,
            };
    }

    #endregion
}
