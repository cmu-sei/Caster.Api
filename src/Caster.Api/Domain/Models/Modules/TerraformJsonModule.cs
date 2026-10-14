// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Caster.Api.Infrastructure.Serialization;

namespace Caster.Api.Domain.Models
{
    // Terraform's JSON configuration syntax for variable and output blocks.
    // Formerly named Gitlab*, but nothing here is Gitlab specific: these parse
    // the contents of variables.tf.json and outputs.tf.json however they were
    // obtained, whether from the Gitlab files API or from a git checkout.

    public static class ModuleVariableResponse
    {
        /// <summary>
        /// Parses a <c>variables.tf.json</c> body.
        /// </summary>
        /// <remarks>
        /// Constraints inherited from the deserialization target, all of which
        /// the Crucible module catalog already satisfies:
        /// <list type="number">
        /// <item>Object form only. Terraform also accepts
        /// <c>"variable": {"x": [{...}]}</c>, which throws here.</item>
        /// <item>The file must contain nothing but <c>variable</c> blocks; a
        /// stray <c>locals</c> breaks the outer dictionary.</item>
        /// <item><c>"default": null</c> is a trap. <see cref="ModuleVariable.IsOptional"/>
        /// is <c>DefaultValue != null</c> and <see cref="NumberToStringConverter"/>
        /// maps JSON null to the empty string, so such a variable reports as
        /// optional with an empty default. Omit the key to make a variable
        /// required.</item>
        /// <item>No <c>object(...)</c> types, because
        /// <see cref="ModuleVariable.GetDefaultValue"/> string-rewrites any type
        /// name containing "object".</item>
        /// </list>
        /// </remarks>
        public static List<ModuleVariable> GetModuleVariables(byte[] jsonResponse)
        {
            List<ModuleVariable> moduleVariables = new List<ModuleVariable>();

            var variables = JsonSerializer
                .Deserialize<Dictionary<string, Dictionary<string, TerraformJsonVariable>>>(
                    jsonResponse,
                    DefaultJsonSettings.Settings);

            foreach (var outerPair in variables)
            {
                if (outerPair.Key == "variable")
                {
                    foreach (var innerPair in outerPair.Value)
                    {
                        innerPair.Value.Name = innerPair.Key;
                        moduleVariables.Add(innerPair.Value.ToModuleVariable());
                    }
                }
            }

            return moduleVariables;
        }
    }

    public class TerraformJsonVariable
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }

        [JsonConverter(typeof(NumberToStringConverter))]
        public string Default { get; set; }

        public ModuleVariable ToModuleVariable()
        {
            return new ModuleVariable()
            {
                Name = this.Name,
                Description = this.Description,
                VariableType = this.Type,
                DefaultValue = this.Default
            };
        }
    }

    public static class ModuleOutputResponse
    {
        /// <summary>
        /// Parses an <c>outputs.tf.json</c> body. The same four constraints
        /// described on <see cref="ModuleVariableResponse.GetModuleVariables"/>
        /// apply. Output <c>value</c> expressions are ignored.
        /// </summary>
        public static List<ModuleOutput> GetModuleOutputs(byte[] jsonResponse)
        {
            List<ModuleOutput> moduleOutputs = new List<ModuleOutput>();

            var outputs = JsonSerializer
                .Deserialize<Dictionary<string, Dictionary<string, TerraformJsonOutput>>>(
                    jsonResponse, DefaultJsonSettings.Settings);

            foreach (var outerPair in outputs)
            {
                if (outerPair.Key == "output")
                {
                    foreach (var innerPair in outerPair.Value)
                    {
                        innerPair.Value.Name = innerPair.Key;
                        moduleOutputs.Add(innerPair.Value.ToModuleOutput());
                    }
                }
            }

            return moduleOutputs;
        }
    }

    public class TerraformJsonOutput
    {
        public string Name { get; set; }
        public string Description { get; set; }

        public ModuleOutput ToModuleOutput()
        {
            return new ModuleOutput()
            {
                Name = this.Name,
                Description = this.Description
            };
        }
    }
}
