// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Serialization;
using File = System.IO.File;

namespace Caster.Api.Tests.Domain.Models.Modules;

/// <summary>Parsing GitLab's project list and a module's <c>variables.tf.json</c>.</summary>
public class GitlabModuleTests
{
    /// <summary>A module's variables as GitLab returns the file: seven variables, each with a default.</summary>
    private const string Variables = """
        {
          "variable": {
            "project_id": { "description": "The guid for the project", "type": "string", "default": "" },
            "user_id": { "description": "The guid of the user deploying this infrastructure", "type": "string", "default": "" },
            "username": { "description": "The username of the user deploying this infrastructure", "type": "string", "default": "" },
            "student": { "description": "The guid of the user team", "type": "string", "default": "" },
            "admin": { "description": "The guid of the admin team", "type": "string", "default": "" },
            "lab_type": { "description": "The type of lab.", "type": "string", "default": "lab" },
            "lab_name": { "description": "The name of this lab", "type": "string", "default": "" }
          }
        }
        """;

    [Fact]
    public void A_gitlab_project_list_deserializes_into_one_module_per_project()
    {
        var modules = JsonSerializer.Deserialize<GitlabModule[]>(Modules(), DefaultJsonSettings.Settings);

        Assert.Equal(6, modules.Length);
    }

    [Fact]
    public void GetModuleVariables_reads_every_variable_of_a_module()
    {
        var variables = GitlabModuleVariableResponse.GetModuleVariables(Encoding.UTF8.GetBytes(Variables));

        Assert.Equal(7, variables.Count());
    }

    /// <summary>
    /// <c>Data/gitlab-modules.json</c> holds GitLab's response as a JSON string literal, the way it was
    /// captured, so it is decoded once before it is parsed as the project list.
    /// </summary>
    private static string Modules() =>
        JsonSerializer.Deserialize<string>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "gitlab-modules.json")));
}
