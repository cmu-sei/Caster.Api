// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Text.Json.Serialization;

namespace Caster.Api.Domain.Models;

public class ModuleVariable
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string VariableType { get; set; }
    public string DefaultValue { get; set; }

    /// <summary>
    /// Returns the DefaultValue in HCL format.
    /// Temporary workaround until we parse HCL directly for variables
    /// </summary>
    public string GetDefaultValue()
    {
        if (VariableType != null && VariableType.ToLower().Contains("object"))
        {
            try
            {
                var stringBuilder = new StringBuilder();

                using (var reader = new StringReader(DefaultValue))
                {
                    for (string line = reader.ReadLine(); line != null; line = reader.ReadLine())
                    {
                        // Remove first set of quotes
                        var firstQuoteIndex = line.IndexOf("\"");

                        if (firstQuoteIndex >= 0)
                        {
                            var secondQuoteIndex = line.IndexOf("\"", firstQuoteIndex + 1);

                            if (secondQuoteIndex >= 0)
                            {
                                line = line.Remove(firstQuoteIndex, 1);
                                line = line.Remove(secondQuoteIndex - 1, 1);
                            }
                        }

                        // Replace first : with =
                        var firstColonIndex = line.IndexOf(':');

                        if (firstColonIndex >= 0)
                        {
                            line = line.Substring(0, firstColonIndex) + " =" + line.Substring(firstColonIndex + 1);
                        }

                        stringBuilder.AppendLine(line);
                    }

                    DefaultValue = stringBuilder.ToString();
                }
            }
            catch (Exception)
            {
                return DefaultValue;
            }
        }

        return DefaultValue;
    }

    [JsonIgnore]
    public bool IsOptional
    {
        get
        {
            return this.DefaultValue != null;
        }
    }

    [JsonIgnore]
    public bool RequiresQuotes
    {
        get
        {
            if (this.VariableType == null)
            {
                return false;
            }

            return this.VariableType.ToLower() == "string";
        }
    }
}

