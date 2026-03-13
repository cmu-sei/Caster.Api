// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Caster.Api.Infrastructure.Serialization;
using Caster.Api.Tests.Integration.Fixtures;

namespace Caster.Api.Tests.Integration.Tests.Controllers;

[Trait("Category", "Integration")]
[Trait("Category", "Projects")]
public class ProjectsControllerTests : IClassFixture<CasterTestContext>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;

    public ProjectsControllerTests(CasterTestContext context)
    {
        _client = context.CreateClient();
        _jsonOptions = DefaultJsonSettings.Settings;
    }

    [Fact]
    public async Task GetAll_ReturnsSuccessStatusCode()
    {
        var response = await _client.GetAsync("/api/projects");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithValidName_ReturnsCreatedProject()
    {
        var command = new { Name = "Integration Test Project" };

        var response = await _client.PostAsJsonAsync("/api/projects", command, _jsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var project = JsonSerializer.Deserialize<ProjectResponse>(content, _jsonOptions);

        Assert.NotNull(project);
        Assert.Equal("Integration Test Project", project.Name);
        Assert.NotEqual(Guid.Empty, project.Id);
    }

    [Fact]
    public async Task Get_WithExistingProject_ReturnsProject()
    {
        // Create a project first
        var createCommand = new { Name = "Get Test Project" };
        var createResponse = await _client.PostAsJsonAsync("/api/projects", createCommand, _jsonOptions);
        var createContent = await createResponse.Content.ReadAsStringAsync();
        var created = JsonSerializer.Deserialize<ProjectResponse>(createContent, _jsonOptions);

        // Now get it
        var response = await _client.GetAsync($"/api/projects/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var project = JsonSerializer.Deserialize<ProjectResponse>(content, _jsonOptions);

        Assert.NotNull(project);
        Assert.Equal(created.Id, project.Id);
        Assert.Equal("Get Test Project", project.Name);
    }

    [Fact]
    public async Task Get_WithNonExistentProject_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/projects/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Edit_WithExistingProject_UpdatesName()
    {
        // Create
        var createCommand = new { Name = "Edit Test Original" };
        var createResponse = await _client.PostAsJsonAsync("/api/projects", createCommand, _jsonOptions);
        var createContent = await createResponse.Content.ReadAsStringAsync();
        var created = JsonSerializer.Deserialize<ProjectResponse>(createContent, _jsonOptions);

        // Edit
        var editCommand = new { Name = "Edit Test Updated" };
        var response = await _client.PutAsJsonAsync($"/api/projects/{created!.Id}", editCommand, _jsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var project = JsonSerializer.Deserialize<ProjectResponse>(content, _jsonOptions);

        Assert.NotNull(project);
        Assert.Equal("Edit Test Updated", project.Name);
    }

    [Fact]
    public async Task Delete_WithExistingProject_ReturnsNoContent()
    {
        // Create
        var createCommand = new { Name = "Delete Test Project" };
        var createResponse = await _client.PostAsJsonAsync("/api/projects", createCommand, _jsonOptions);
        var createContent = await createResponse.Content.ReadAsStringAsync();
        var created = JsonSerializer.Deserialize<ProjectResponse>(createContent, _jsonOptions);

        // Delete
        var response = await _client.DeleteAsync($"/api/projects/{created!.Id}");

        Assert.True(
            response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.OK,
            $"Expected NoContent or OK, got {response.StatusCode}");

        // Verify deleted
        var getResponse = await _client.GetAsync($"/api/projects/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    /// <summary>
    /// Minimal DTO for deserializing project responses in integration tests.
    /// </summary>
    private class ProjectResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
