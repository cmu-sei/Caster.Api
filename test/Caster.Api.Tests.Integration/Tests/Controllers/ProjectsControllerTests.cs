// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Caster.Api.Infrastructure.Serialization;
using Caster.Api.Tests.Integration.Fixtures;

namespace Caster.Api.Tests.Integration.Tests.Controllers;

[Category("Integration")]
[Category("Projects")]
[ClassDataSource<CasterTestContext>(Shared = SharedType.PerTestSession)]
public class ProjectsControllerTests(CasterTestContext context)
{
    private readonly HttpClient _client = context.CreateClient();
    private readonly JsonSerializerOptions _jsonOptions = DefaultJsonSettings.Settings;

    [Test]
    public async Task GetAll_WhenCalled_ReturnsSuccessStatusCode()
    {
        var response = await _client.GetAsync("/api/projects");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Create_WithValidName_ReturnsCreatedProject()
    {
        var command = new { Name = "Integration Test Project" };

        var response = await _client.PostAsJsonAsync("/api/projects", command, _jsonOptions);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var project = JsonSerializer.Deserialize<ProjectResponse>(content, _jsonOptions);

        await Assert.That(project).IsNotNull();
        await Assert.That(project.Name).IsEqualTo("Integration Test Project");
        await Assert.That(project.Id).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task Get_WithExistingProject_ReturnsProject()
    {
        // Create a project first
        var createCommand = new { Name = "Get Test Project" };
        var createResponse = await _client.PostAsJsonAsync("/api/projects", createCommand, _jsonOptions);
        var createContent = await createResponse.Content.ReadAsStringAsync();
        var created = JsonSerializer.Deserialize<ProjectResponse>(createContent, _jsonOptions);

        // Now get it
        var response = await _client.GetAsync($"/api/projects/{created!.Id}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var project = JsonSerializer.Deserialize<ProjectResponse>(content, _jsonOptions);

        await Assert.That(project).IsNotNull();
        await Assert.That(project.Id).IsEqualTo(created.Id);
        await Assert.That(project.Name).IsEqualTo("Get Test Project");
    }

    [Test]
    public async Task Get_WithNonExistentProject_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/projects/{Guid.NewGuid()}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
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

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var project = JsonSerializer.Deserialize<ProjectResponse>(content, _jsonOptions);

        await Assert.That(project).IsNotNull();
        await Assert.That(project.Name).IsEqualTo("Edit Test Updated");
    }

    [Test]
    public async Task Delete_WithExistingProject_RemovesProjectSuccessfully()
    {
        // Create
        var createCommand = new { Name = "Delete Test Project" };
        var createResponse = await _client.PostAsJsonAsync("/api/projects", createCommand, _jsonOptions);
        var createContent = await createResponse.Content.ReadAsStringAsync();
        var created = JsonSerializer.Deserialize<ProjectResponse>(createContent, _jsonOptions);

        // Delete
        var response = await _client.DeleteAsync($"/api/projects/{created!.Id}");

        await Assert.That(response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.OK)
            .IsTrue();

        // Verify deleted
        var getResponse = await _client.GetAsync($"/api/projects/{created.Id}");
        await Assert.That(getResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
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
