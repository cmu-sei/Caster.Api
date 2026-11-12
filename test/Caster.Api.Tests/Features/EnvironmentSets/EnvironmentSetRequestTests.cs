// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Caster.Api.Domain.Models;
using Caster.Api.Features.EnvironmentSets.EventHandlers;
using Caster.Api.Hubs;
using Caster.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using ProjectSetView = Caster.Api.Features.EnvironmentSets.ProjectEnvironmentSet;
using SecretsView = Caster.Api.Features.EnvironmentSets.EnvironmentSecrets;
using SetView = Caster.Api.Features.EnvironmentSets.EnvironmentSet;

namespace Caster.Api.Tests.Features.EnvironmentSets;

/// <summary>
/// <c>api/environment-sets</c> and <c>api/projects/{id}/environment-sets</c>: reading sets needs
/// ViewEnvironmentSets, every write and every assignment ManageEnvironmentSets, and a project's own sets
/// are readable with ViewProject (or ViewProjects) without their secret source names.
/// </summary>
public class EnvironmentSetRequestTests(DatabaseFixture fixture, CasterAppFactory factory) : ApiTestBase(fixture, factory)
{
    private const string Sets = "api/environment-sets";

    // ---- GET api/environment-sets ---------------------------------------------------------------

    [Fact]
    public async Task GetAll_lists_the_sets_with_secret_source_names_for_a_caller_holding_ViewEnvironmentSets()
    {
        using var secret = new SecretEnvironmentVariable("s3cr3t-token-value");
        var (set, _) = await SeedSet(secret.Name);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEnvironmentSets).SeedAsync();

        var response = await Client(actor).GetAsync(Sets, Ct);

        var body = await response.Content.ReadAsStringAsync(Ct);
        var view = Assert.Single(await ReadAsync<SetView[]>(response), x => x.Id == set.Id);
        Assert.Equal(secret.Name, view.Entries.Single(x => x.ValueSource == EnvironmentSetValueSource.Secret).SecretName);
        Assert.True(view.IsComplete);
        Assert.DoesNotContain(secret.Value, body);
    }

    /// <summary>ManageEnvironmentSets implies ViewEnvironmentSets, so a manager can use the screens.</summary>
    [Fact]
    public async Task GetAll_lists_the_sets_to_a_caller_holding_only_ManageEnvironmentSets()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEnvironmentSets).SeedAsync();

        var response = await Client(actor).GetAsync(Sets, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    [Fact]
    public async Task GetAll_lists_the_sets_to_the_seeded_Observer_role()
    {
        var (set, _) = await SeedSet(SecretEnvironmentVariable.Missing());
        var actor = await Actor().WithRole(TestData.Roles.Observer).SeedAsync();

        var response = await Client(actor).GetAsync(Sets, Ct);

        Assert.Contains(await ReadAsync<SetView[]>(response), x => x.Id == set.Id);
    }

    [Fact]
    public async Task The_seeded_Observer_role_holds_ViewEnvironmentSets_but_not_ManageEnvironmentSets()
    {
        var observer = await ReadBack(db => db.SystemRoles.SingleAsync(x => x.Id == TestData.Roles.Observer, Ct));

        Assert.Contains(SystemPermission.ViewEnvironmentSets, observer.Permissions);
        Assert.DoesNotContain(SystemPermission.ManageEnvironmentSets, observer.Permissions);
    }

    // ---- GET api/environment-sets/{id} ----------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_set_marked_incomplete_when_a_secret_source_is_missing()
    {
        var (set, _) = await SeedSet(SecretEnvironmentVariable.Missing());
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEnvironmentSets).SeedAsync();

        var view = await ReadAsync<SetView>(await Client(actor).GetAsync($"{Sets}/{set.Id}", Ct));

        Assert.False(view.IsComplete);
        Assert.False(view.Entries.Single(x => x.ValueSource == EnvironmentSetValueSource.Secret).IsAvailable);
    }

    [Fact]
    public async Task Get_returns_the_set_to_a_caller_holding_only_ManageEnvironmentSets()
    {
        var (set, _) = await SeedSet(SecretEnvironmentVariable.Missing());
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEnvironmentSets).SeedAsync();

        var response = await Client(actor).GetAsync($"{Sets}/{set.Id}", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    [Fact]
    public async Task Get_answers_404_for_a_set_that_does_not_exist()
    {
        var response = await RootClient.GetAsync($"{Sets}/{Guid.NewGuid()}", Ct);

        await AssertProblem(HttpStatusCode.NotFound, response);
    }

    // ---- GET api/environment-sets/secrets -------------------------------------------------------

    [Fact]
    public async Task GetSecrets_lists_secret_names_and_missing_sources_but_never_values()
    {
        using var secret = new SecretEnvironmentVariable("s3cr3t-token-value");
        var missing = SecretEnvironmentVariable.Missing();
        await SeedSet(missing);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEnvironmentSets).SeedAsync();

        var response = await Client(actor).GetAsync($"{Sets}/secrets", Ct);

        var body = await response.Content.ReadAsStringAsync(Ct);
        var view = await ReadAsync<SecretsView>(response);
        Assert.Contains(secret.Name, view.Available);
        Assert.Contains(missing, view.Missing);
        Assert.DoesNotContain(secret.Value, body);
        Assert.DoesNotContain(view.Available, x => !x.StartsWith("CASTER_SECRET_", StringComparison.Ordinal));
        Assert.Equal(["^CASTER_SECRET_"], view.Patterns);
        Assert.Equal(Caster.Api.Features.EnvironmentSets.TerraformRunMode.Process, view.RunMode);
        Assert.False(view.InheritAll);
    }

    [Fact]
    public async Task GetSecrets_answers_a_caller_holding_only_ManageEnvironmentSets()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEnvironmentSets).SeedAsync();

        var response = await Client(actor).GetAsync($"{Sets}/secrets", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    // ---- POST, PUT, DELETE api/environment-sets -------------------------------------------------

    [Fact]
    public async Task Create_by_a_caller_holding_ManageEnvironmentSets_stores_the_set()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEnvironmentSets).SeedAsync();
        var name = $"vsphere-{Guid.NewGuid():N}";

        var response = await Client(actor).PostAsJsonAsync(Sets, new { name, description = "vCenter credentials" }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        var stored = await ReadBack(db => db.EnvironmentSets.SingleAsync(x => x.Name == name, Ct));
        Assert.Equal("vCenter credentials", stored.Description);
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_only_ViewEnvironmentSets()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEnvironmentSets).SeedAsync();
        var name = $"vsphere-{Guid.NewGuid():N}";

        var response = await Client(actor).PostAsJsonAsync(Sets, new { name }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False(await ReadBack(db => db.EnvironmentSets.AnyAsync(x => x.Name == name, Ct)));
    }

    [Fact]
    public async Task Create_refuses_a_name_another_set_has()
    {
        var existing = TestData.EnvironmentSet();
        await Seed(existing);

        var response = await RootClient.PostAsJsonAsync(Sets, new { name = existing.Name.ToUpperInvariant() }, Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
    }

    [Fact]
    public async Task Create_refuses_an_empty_name()
    {
        var response = await RootClient.PostAsJsonAsync(Sets, new { name = "" }, Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
    }

    [Fact]
    public async Task Edit_by_a_caller_holding_ManageEnvironmentSets_stores_the_change()
    {
        var set = TestData.EnvironmentSet();
        await Seed(set);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEnvironmentSets).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"{Sets}/{set.Id}", new { name = set.Name, description = "changed" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("changed", (await StoredSet(set.Id)).Description);
    }

    [Fact]
    public async Task Edit_is_forbidden_for_a_caller_holding_only_ViewEnvironmentSets()
    {
        var set = TestData.EnvironmentSet();
        await Seed(set);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEnvironmentSets).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"{Sets}/{set.Id}", new { name = set.Name, description = "changed" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Null((await StoredSet(set.Id)).Description);
    }

    [Fact]
    public async Task Delete_by_a_caller_holding_ManageEnvironmentSets_removes_the_set_from_its_projects()
    {
        var (set, project) = await SeedSet(SecretEnvironmentVariable.Missing(), assign: true);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEnvironmentSets).SeedAsync();

        var response = await Client(actor).DeleteAsync($"{Sets}/{set.Id}", Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.Null(await StoredSet(set.Id));
        Assert.False(await ReadBack(db => db.ProjectEnvironmentSets.AnyAsync(x => x.ProjectId == project.Id, Ct)));
        Assert.False(await ReadBack(db => db.EnvironmentSetEntries.AnyAsync(x => x.EnvironmentSetId == set.Id, Ct)));
    }

    [Fact]
    public async Task Delete_tells_the_projects_it_was_assigned_to_and_the_admins()
    {
        var (set, project) = await SeedSet(SecretEnvironmentVariable.Missing(), assign: true);

        await AssertStatus(HttpStatusCode.NoContent, await RootClient.DeleteAsync($"{Sets}/{set.Id}", Ct));

        var projectBroadcast = Assert.Single(Factory.Hub<ProjectHub>().ToGroup(project.Id));
        Assert.Equal(EnvironmentSetHubMethods.ProjectEnvironmentSetsUpdated, projectBroadcast.Method);
        Assert.Empty(Assert.IsType<ProjectSetView[]>(projectBroadcast.Arguments[1]));
        Assert.Contains(
            Factory.Hub<ProjectHub>().ToGroup(nameof(HubGroups.EnvironmentSetsAdmin)),
            x => x.Method == EnvironmentSetHubMethods.EnvironmentSetDeleted && Equals(x.Argument, set.Id));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_only_ViewEnvironmentSets()
    {
        var set = TestData.EnvironmentSet();
        await Seed(set);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEnvironmentSets).SeedAsync();

        var response = await Client(actor).DeleteAsync($"{Sets}/{set.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredSet(set.Id));
    }

    // ---- entries --------------------------------------------------------------------------------

    [Fact]
    public async Task CreateEntry_by_a_caller_holding_ManageEnvironmentSets_stores_a_secret_entry()
    {
        using var secret = new SecretEnvironmentVariable("s3cr3t-token-value");
        var set = TestData.EnvironmentSet();
        await Seed(set);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEnvironmentSets).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(
            $"{Sets}/{set.Id}/entries",
            new { kind = "EnvironmentVariable", targetName = "VSPHERE_PASSWORD", valueSource = "Secret", secretName = secret.Name },
            Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var stored = await ReadBack(db => db.EnvironmentSetEntries.SingleAsync(x => x.EnvironmentSetId == set.Id, Ct));
        Assert.Equal(EnvironmentSetEntryKind.EnvironmentVariable, stored.Kind);
        Assert.Equal(secret.Name, stored.SecretName);
        Assert.Null(stored.Value);
    }

    /// <summary>Terraform saves variables in plans and state, so a secret can't be one.</summary>
    [Fact]
    public async Task CreateEntry_refuses_a_secret_Terraform_variable()
    {
        using var secret = new SecretEnvironmentVariable("s3cr3t-token-value");
        var set = TestData.EnvironmentSet();
        await Seed(set);

        var response = await RootClient.PostAsJsonAsync(
            $"{Sets}/{set.Id}/entries",
            new { kind = "TerraformVariable", targetName = "vsphere_password", valueSource = "Secret", secretName = secret.Name },
            Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
    }

    [Fact]
    public async Task CreateEntry_is_forbidden_for_a_caller_holding_only_ViewEnvironmentSets()
    {
        var set = TestData.EnvironmentSet();
        await Seed(set);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEnvironmentSets).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync(
            $"{Sets}/{set.Id}/entries", new { kind = "EnvironmentVariable", targetName = "REGION", valueSource = "Plain", value = "east" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False(await ReadBack(db => db.EnvironmentSetEntries.AnyAsync(x => x.EnvironmentSetId == set.Id, Ct)));
    }

    [Theory]
    [InlineData("EnvironmentVariable", "TF_CLI_ARGS", "Plain", null, "-auto-approve")]
    [InlineData("EnvironmentVariable", "TF_DATA_DIR", "Plain", null, "/tmp")]
    [InlineData("EnvironmentVariable", "TF_VAR_password", "Plain", null, "x")]
    [InlineData("EnvironmentVariable", "HOME", "Plain", null, "/tmp")]
    [InlineData("EnvironmentVariable", "PATH", "Plain", null, "/tmp")]
    [InlineData("EnvironmentVariable", "BAD-NAME", "Plain", null, "x")]
    [InlineData("TerraformVariable", "2fa", "Plain", null, "x")]
    [InlineData("EnvironmentVariable", "TOKEN", "Secret", "ConnectionStrings__PostgreSQL", null)]
    [InlineData("EnvironmentVariable", "TOKEN", "Secret", "AUTHORIZATION_CLIENTSECRET", null)]
    [InlineData("EnvironmentVariable", "TOKEN", "Secret", null, null)]
    [InlineData("EnvironmentVariable", "TOKEN", "Secret", "CASTER_SECRET_TOKEN", "stored")]
    [InlineData("EnvironmentVariable", "TOKEN", "Plain", "CASTER_SECRET_TOKEN", "x")]
    [InlineData("EnvironmentVariable", "TOKEN", "Plain", null, null)]
    public async Task CreateEntry_refuses_an_entry_the_rules_do_not_allow(string kind, string targetName, string valueSource, string secretName, string value)
    {
        var set = TestData.EnvironmentSet();
        await Seed(set);

        var response = await RootClient.PostAsJsonAsync(
            $"{Sets}/{set.Id}/entries", new { kind, targetName, valueSource, secretName, value }, Ct);

        await AssertProblem(HttpStatusCode.BadRequest, response);
        Assert.False(await ReadBack(db => db.EnvironmentSetEntries.AnyAsync(x => x.EnvironmentSetId == set.Id, Ct)));
    }

    [Fact]
    public async Task CreateEntry_allows_a_TF_TOKEN_variable_for_private_registry_credentials()
    {
        var set = TestData.EnvironmentSet();
        await Seed(set);

        var response = await RootClient.PostAsJsonAsync(
            $"{Sets}/{set.Id}/entries",
            new { kind = "EnvironmentVariable", targetName = "TF_TOKEN_registry_example_com", valueSource = "Secret", secretName = SecretEnvironmentVariable.Missing() },
            Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    [Fact]
    public async Task CreateEntry_refuses_a_target_the_set_already_has()
    {
        var set = TestData.EnvironmentSet();
        await Seed(set, TestData.PlainEntry(set, "REGION", "east"));

        var response = await RootClient.PostAsJsonAsync(
            $"{Sets}/{set.Id}/entries", new { kind = "EnvironmentVariable", targetName = "REGION", valueSource = "Plain", value = "west" }, Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
    }

    [Fact]
    public async Task CreateEntry_refuses_a_target_another_set_on_an_assigned_project_already_sets()
    {
        var project = TestData.Project();
        var first = TestData.EnvironmentSet("first");
        var second = TestData.EnvironmentSet("second");
        await Seed(
            project, first, second,
            TestData.PlainEntry(first, "REGION", "east"),
            TestData.ProjectEnvironmentSet(project, first),
            TestData.ProjectEnvironmentSet(project, second));

        var response = await RootClient.PostAsJsonAsync(
            $"{Sets}/{second.Id}/entries", new { kind = "EnvironmentVariable", targetName = "REGION", valueSource = "Plain", value = "west" }, Ct);

        var problem = await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Contains("\"first\"", problem.Detail ?? problem.Title);
    }

    [Fact]
    public async Task EditEntry_by_a_caller_holding_ManageEnvironmentSets_stores_the_change()
    {
        var set = TestData.EnvironmentSet();
        var entry = TestData.PlainEntry(set, "REGION", "east");
        await Seed(set, entry);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEnvironmentSets).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync(
            $"{Sets}/{set.Id}/entries/{entry.Id}", new { kind = "EnvironmentVariable", targetName = "REGION", valueSource = "Plain", value = "west" }, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("west", (await StoredEntry(entry.Id)).Value);
    }

    [Fact]
    public async Task EditEntry_is_forbidden_for_a_caller_holding_only_ViewEnvironmentSets()
    {
        var set = TestData.EnvironmentSet();
        var entry = TestData.PlainEntry(set, "REGION", "east");
        await Seed(set, entry);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEnvironmentSets).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync(
            $"{Sets}/{set.Id}/entries/{entry.Id}", new { kind = "EnvironmentVariable", targetName = "REGION", valueSource = "Plain", value = "west" }, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.Equal("east", (await StoredEntry(entry.Id)).Value);
    }

    [Fact]
    public async Task EditEntry_refuses_a_rename_that_collides_on_an_assigned_project_and_keeps_the_entry()
    {
        var project = TestData.Project();
        var first = TestData.EnvironmentSet("first");
        var second = TestData.EnvironmentSet("second");
        var entry = TestData.PlainEntry(second, "ZONE", "a");
        await Seed(
            project, first, second, entry,
            TestData.PlainEntry(first, "REGION", "east"),
            TestData.ProjectEnvironmentSet(project, first),
            TestData.ProjectEnvironmentSet(project, second));

        var response = await RootClient.PutAsJsonAsync(
            $"{Sets}/{second.Id}/entries/{entry.Id}", new { kind = "EnvironmentVariable", targetName = "REGION", valueSource = "Plain", value = "a" }, Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.Equal("ZONE", (await StoredEntry(entry.Id)).TargetName);
    }

    [Fact]
    public async Task EditEntry_answers_404_for_an_entry_of_another_set()
    {
        var set = TestData.EnvironmentSet();
        var other = TestData.EnvironmentSet();
        var entry = TestData.PlainEntry(other, "REGION", "east");
        await Seed(set, other, entry);

        var response = await RootClient.PutAsJsonAsync(
            $"{Sets}/{set.Id}/entries/{entry.Id}", new { kind = "EnvironmentVariable", targetName = "REGION", valueSource = "Plain", value = "west" }, Ct);

        await AssertProblem(HttpStatusCode.NotFound, response);
        Assert.Equal("east", (await StoredEntry(entry.Id)).Value);
    }

    [Fact]
    public async Task DeleteEntry_by_a_caller_holding_ManageEnvironmentSets_removes_the_entry()
    {
        var set = TestData.EnvironmentSet();
        var entry = TestData.PlainEntry(set, "REGION", "east");
        await Seed(set, entry);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEnvironmentSets).SeedAsync();

        var response = await Client(actor).DeleteAsync($"{Sets}/{set.Id}/entries/{entry.Id}", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Null(await StoredEntry(entry.Id));
    }

    [Fact]
    public async Task DeleteEntry_is_forbidden_for_a_caller_holding_only_ViewEnvironmentSets()
    {
        var set = TestData.EnvironmentSet();
        var entry = TestData.PlainEntry(set, "REGION", "east");
        await Seed(set, entry);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEnvironmentSets).SeedAsync();

        var response = await Client(actor).DeleteAsync($"{Sets}/{set.Id}/entries/{entry.Id}", Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.NotNull(await StoredEntry(entry.Id));
    }

    // ---- GET api/projects/{id}/environment-sets -------------------------------------------------

    [Fact]
    public async Task GetByProject_shows_a_member_holding_ViewProject_the_sets_without_secret_source_names()
    {
        using var secret = new SecretEnvironmentVariable("s3cr3t-token-value");
        var (set, project) = await SeedSet(secret.Name, assign: true);
        var actor = await Actor().OnProject(project, [ProjectPermission.ViewProject]).SeedAsync();

        var response = await Client(actor).GetAsync(ProjectSets(project.Id), Ct);

        var body = await response.Content.ReadAsStringAsync(Ct);
        var view = Assert.Single(await ReadAsync<ProjectSetView[]>(response));
        Assert.Equal(set.Name, view.Name);
        Assert.True(view.IsComplete);
        Assert.Equal("east", view.Entries.Single(x => x.TargetName == "REGION").Value);
        var secretEntry = view.Entries.Single(x => x.TargetName == "TOKEN");
        Assert.True(secretEntry.IsAvailable);
        Assert.Null(secretEntry.Value);
        Assert.DoesNotContain(secret.Name, body);
        Assert.DoesNotContain(secret.Value, body);
    }

    [Fact]
    public async Task GetByProject_shows_a_caller_holding_ViewProjects_the_sets()
    {
        var (_, project) = await SeedSet(SecretEnvironmentVariable.Missing(), assign: true);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewProjects).SeedAsync();

        var view = Assert.Single(await ReadAsync<ProjectSetView[]>(await Client(actor).GetAsync(ProjectSets(project.Id), Ct)));

        Assert.False(view.IsComplete);
    }

    [Fact]
    public async Task GetByProject_is_forbidden_for_a_caller_holding_ViewProject_only_on_another_project()
    {
        var (_, project) = await SeedSet(SecretEnvironmentVariable.Missing(), assign: true);
        var actor = await Actor().OnNewProject(ProjectPermission.ViewProject).SeedAsync();

        var response = await Client(actor).GetAsync(ProjectSets(project.Id), Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task GetByProject_is_forbidden_for_a_caller_holding_only_ViewEnvironmentSets()
    {
        var (_, project) = await SeedSet(SecretEnvironmentVariable.Missing(), assign: true);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEnvironmentSets).SeedAsync();

        var response = await Client(actor).GetAsync(ProjectSets(project.Id), Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    // ---- assign and unassign --------------------------------------------------------------------

    [Fact]
    public async Task Assign_by_a_caller_holding_ManageEnvironmentSets_assigns_the_set_and_tells_the_project()
    {
        var (set, _) = await SeedSet(SecretEnvironmentVariable.Missing());
        var project = TestData.Project();
        await Seed(project);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEnvironmentSets).SeedAsync();

        var response = await Client(actor).PostAsync(Assignment(project.Id, set.Id), null, Ct);

        Assert.Equal(set.Id, Assert.Single(await ReadAsync<ProjectSetView[]>(response)).EnvironmentSetId);
        Assert.True(await ReadBack(db => db.ProjectEnvironmentSets.AnyAsync(x => x.ProjectId == project.Id && x.EnvironmentSetId == set.Id, Ct)));

        var broadcast = Assert.Single(Factory.Hub<ProjectHub>().ToGroup(project.Id));
        Assert.Equal(EnvironmentSetHubMethods.ProjectEnvironmentSetsUpdated, broadcast.Method);
        Assert.Equal(project.Id, broadcast.Arguments[0]);
        Assert.Null(Assert.Single(Assert.IsType<ProjectSetView[]>(broadcast.Arguments[1])).Entries.Single(x => x.TargetName == "TOKEN").Value);
    }

    [Fact]
    public async Task Assign_is_forbidden_for_a_project_manager_without_ManageEnvironmentSets()
    {
        var (set, _) = await SeedSet(SecretEnvironmentVariable.Missing());
        var project = TestData.Project();
        await Seed(project);
        var actor = await Actor().OnProject(project, [ProjectPermission.ManageProject]).SeedAsync();

        var response = await Client(actor).PostAsync(Assignment(project.Id, set.Id), null, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.False(await ReadBack(db => db.ProjectEnvironmentSets.AnyAsync(x => x.ProjectId == project.Id, Ct)));
    }

    [Fact]
    public async Task Assign_is_forbidden_for_a_caller_holding_only_ViewEnvironmentSets()
    {
        var (set, _) = await SeedSet(SecretEnvironmentVariable.Missing());
        var project = TestData.Project();
        await Seed(project);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEnvironmentSets).SeedAsync();

        var response = await Client(actor).PostAsync(Assignment(project.Id, set.Id), null, Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Assign_refuses_a_set_whose_target_another_set_on_the_project_already_sets()
    {
        var project = TestData.Project();
        var first = TestData.EnvironmentSet("first");
        var second = TestData.EnvironmentSet("second");
        await Seed(
            project, first, second,
            TestData.PlainEntry(first, "REGION", "east"),
            TestData.PlainEntry(second, "REGION", "west"),
            TestData.ProjectEnvironmentSet(project, first));

        var response = await RootClient.PostAsync(Assignment(project.Id, second.Id), null, Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.False(await ReadBack(db => db.ProjectEnvironmentSets.AnyAsync(x => x.EnvironmentSetId == second.Id, Ct)));
    }

    [Fact]
    public async Task Assign_refuses_a_set_whose_secret_source_no_longer_matches_the_patterns()
    {
        var project = TestData.Project();
        var set = TestData.EnvironmentSet();
        await Seed(project, set, TestData.SecretEntry(set, "TOKEN", "ConnectionStrings__PostgreSQL"));

        var response = await RootClient.PostAsync(Assignment(project.Id, set.Id), null, Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
        Assert.False(await ReadBack(db => db.ProjectEnvironmentSets.AnyAsync(x => x.ProjectId == project.Id, Ct)));
    }

    [Fact]
    public async Task Assign_refuses_a_set_already_assigned()
    {
        var (set, project) = await SeedSet(SecretEnvironmentVariable.Missing(), assign: true);

        var response = await RootClient.PostAsync(Assignment(project.Id, set.Id), null, Ct);

        await AssertProblem(HttpStatusCode.Conflict, response);
    }

    [Fact]
    public async Task Unassign_by_a_caller_holding_ManageEnvironmentSets_removes_the_assignment()
    {
        var (set, project) = await SeedSet(SecretEnvironmentVariable.Missing(), assign: true);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEnvironmentSets).SeedAsync();

        var response = await Client(actor).DeleteAsync(Assignment(project.Id, set.Id), Ct);

        await AssertStatus(HttpStatusCode.NoContent, response);
        Assert.False(await ReadBack(db => db.ProjectEnvironmentSets.AnyAsync(x => x.ProjectId == project.Id, Ct)));
        Assert.NotNull(await StoredSet(set.Id));
    }

    [Fact]
    public async Task Unassign_is_forbidden_for_a_caller_holding_only_ViewEnvironmentSets()
    {
        var (set, project) = await SeedSet(SecretEnvironmentVariable.Missing(), assign: true);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEnvironmentSets).SeedAsync();

        var response = await Client(actor).DeleteAsync(Assignment(project.Id, set.Id), Ct);

        await AssertProblem(HttpStatusCode.Forbidden, response);
        Assert.True(await ReadBack(db => db.ProjectEnvironmentSets.AnyAsync(x => x.ProjectId == project.Id, Ct)));
    }

    // ---- export ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_project_export_carries_none_of_its_sets()
    {
        var (set, project) = await SeedSet(SecretEnvironmentVariable.Missing(), assign: true);
        var directory = TestData.Directory(project);
        await Seed(directory, TestData.Workspace(directory));

        var response = await RootClient.GetAsync($"api/projects/{project.Id}/actions/export?archiveType=zip&includeSettings=true", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        using var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync(Ct)));
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            var content = await reader.ReadToEndAsync(Ct);
            Assert.DoesNotContain(set.Name, content);
            Assert.DoesNotContain("REGION", content);
        }
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static string ProjectSets(Guid projectId) => $"api/projects/{projectId}/environment-sets";

    private static string Assignment(Guid projectId, Guid setId) => $"api/projects/{projectId}/environment-sets/{setId}";

    /// <summary>A set with a plain REGION and a secret TOKEN read from <paramref name="secretName"/>, optionally on a project.</summary>
    private async Task<(EnvironmentSet Set, Project Project)> SeedSet(string secretName, bool assign = false)
    {
        var set = TestData.EnvironmentSet();
        var project = TestData.Project();
        await Seed(set, project, TestData.PlainEntry(set, "REGION", "east"), TestData.SecretEntry(set, "TOKEN", secretName));

        if (assign)
        {
            await Seed(TestData.ProjectEnvironmentSet(project, set));
        }

        return (set, project);
    }

    private Task<EnvironmentSet> StoredSet(Guid id) =>
        ReadBack(db => db.EnvironmentSets.SingleOrDefaultAsync(x => x.Id == id, Ct));

    private Task<EnvironmentSetEntry> StoredEntry(Guid id) =>
        ReadBack(db => db.EnvironmentSetEntries.SingleOrDefaultAsync(x => x.Id == id, Ct));
}
