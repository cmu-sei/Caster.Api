// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Caster.Api.Infrastructure.Serialization;

namespace Caster.Api.Domain.Models
{
    public class GitlabModule
    {
        public int Id { get; set; }
        public string Name { get; set; }

        [JsonPropertyName("path_with_namespace")]
        public string Path { get; set; }
        public string Description { get; set; }

        [JsonPropertyName("last_activity_at")]
        public DateTime LastActivityAt { get; set; }

        [JsonPropertyName("http_url_to_repo")]
        public string RepoUrl { get; set; }

        public Module ToModule(DateTime requestTime)
        {
            return new Module()
            {
                Name = this.Name,
                Path = this.Path,
                Description = this.Description,
                DateModified = requestTime
            };
        }
    }

    public class GitlabRelease
    {
        [JsonPropertyName("tag_name")]
        public string Name { get; set; }

        [JsonPropertyName("released_at")]
        public DateTime DateCreated { get; set; }
    }

    public static class GitlabModuleVariableResponse
    {
        public static List<ModuleVariable> GetModuleVariables(byte[] jsonResponse)
        {
            List<ModuleVariable> moduleVariables = new List<ModuleVariable>();

            var variables = JsonSerializer
                .Deserialize<Dictionary<string, Dictionary<string, GitlabModuleVariable>>>(
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

    public class GitlabModuleVariable
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

    public static class GitlabModuleOutputResponse
    {
        public static List<ModuleOutput> GetModuleOutputs(byte[] jsonResponse)
        {
            List<ModuleOutput> moduleOutputs = new List<ModuleOutput>();

            var outputs = System.Text.Json.JsonSerializer
            .Deserialize<Dictionary<string, Dictionary<string, GitlabModuleOutput>>>(
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

    public class GitlabModuleOutput
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
