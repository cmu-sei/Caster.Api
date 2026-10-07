// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Text.Json.Serialization;

namespace Caster.Api.Domain.Models
{
    // Response shapes specific to the Gitlab REST API. The variable and output
    // parsing that used to live here moved to TerraformJsonModule.cs, because
    // it reads Terraform's JSON syntax and has nothing to do with Gitlab.

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
}
