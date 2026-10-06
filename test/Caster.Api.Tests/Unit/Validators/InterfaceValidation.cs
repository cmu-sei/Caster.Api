// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Caster.Api.Domain.Services;
using Caster.Api.Features.Shared.Behaviors;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Options;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using ApiValidationException = Caster.Api.Infrastructure.Exceptions.ValidationException;
using Directories = Caster.Api.Features.Directories;
using Workspaces = Caster.Api.Features.Workspaces;

namespace Caster.Api.Tests.Unit.Validators
{
    /// <summary>
    /// Runs commands through ValidationBehavior with validators registered the same way
    /// as Startup, so validators declared over an interface that the command implements
    /// (IWorkspaceUpdateRequest, IDirectoryUpdateRequest) must be resolved and run.
    /// </summary>
    [Trait("Category", "Unit")]
    [Trait("Category", "Validation")]
    public class InterfaceValidationTest
    {
        private const string InstalledVersion = "1.5.7";

        private readonly IServiceProvider _services;

        public InterfaceValidationTest()
        {
            var validationService = Substitute.For<IValidationService>();
            validationService.DirectoryExists(Arg.Any<Guid>()).Returns(Task.FromResult(true));
            validationService.ProjectExists(Arg.Any<Guid>()).Returns(Task.FromResult(true));

            var terraformService = Substitute.For<ITerraformService>();
            terraformService.IsValidVersion(InstalledVersion).Returns(true);

            _services = new ServiceCollection()
                .AddValidatorsFromAssemblyContaining<Startup>()
                .AddSingleton(validationService)
                .AddSingleton(terraformService)
                .AddSingleton(new TerraformOptions { MaxParallelism = 10 })
                .BuildServiceProvider();
        }

        public static IEnumerable<object[]> CommandsWithVersion(string version) =>
        [
            [new Workspaces.Create.Command { Name = "valid", DirectoryId = Guid.NewGuid(), TerraformVersion = version }],
            [new Workspaces.Edit.Command { Id = Guid.NewGuid(), Name = "valid", DirectoryId = Guid.NewGuid(), TerraformVersion = version }],
            [new Workspaces.PartialEdit.Command { Id = Guid.NewGuid(), TerraformVersion = version }],
            [new Directories.Create.Command { Name = "valid", ProjectId = Guid.NewGuid(), TerraformVersion = version }],
            [new Directories.Edit.Command { Id = Guid.NewGuid(), Name = "valid", TerraformVersion = version }],
            [new Directories.PartialEdit.Command { Id = Guid.NewGuid(), TerraformVersion = version }],
        ];

        public static IEnumerable<object[]> InvalidVersionCommands => CommandsWithVersion("../../usr/bin");
        public static IEnumerable<object[]> ValidVersionCommands => CommandsWithVersion(InstalledVersion);

        [Theory]
        [MemberData(nameof(InvalidVersionCommands))]
        public async Task Test_Rejects_Uninstalled_TerraformVersion(object command)
        {
            var errors = await ValidateObject(command);

            Assert.True(errors.ContainsKey("TerraformVersion"));
            // The interface validator must run once, not once per lookup
            Assert.Single(errors["TerraformVersion"]);
        }

        [Theory]
        [MemberData(nameof(ValidVersionCommands))]
        public async Task Test_Accepts_Installed_TerraformVersion(object command)
        {
            var errors = await ValidateObject(command);

            Assert.Empty(errors);
        }

        [Theory]
        [InlineData("bad name!!")]
        [InlineData("café")]
        [InlineData("../escape")]
        [InlineData("a+b@c:d")]
        [InlineData("smith,_john")]
        [InlineData("")]
        public async Task Test_Workspace_Commands_Reject_Invalid_Names(string name)
        {
            var commands = new object[]
            {
                new Workspaces.Create.Command { Name = name, DirectoryId = Guid.NewGuid() },
                new Workspaces.Edit.Command { Id = Guid.NewGuid(), Name = name, DirectoryId = Guid.NewGuid() },
                new Workspaces.PartialEdit.Command { Id = Guid.NewGuid(), Name = name },
            };

            foreach (var command in commands)
            {
                var errors = await ValidateObject(command);
                Assert.True(errors.ContainsKey("Name"), $"{command.GetType().FullName} accepted '{name}'");
            }
        }

        [Fact]
        public async Task Test_Workspace_Commands_Reject_Names_Over_90_Characters()
        {
            var errors = await Validate(new Workspaces.Create.Command { Name = new string('a', 91), DirectoryId = Guid.NewGuid() });

            Assert.True(errors.ContainsKey("Name"));
        }

        [Theory]
        [InlineData("ok.name_1-2")]
        [InlineData("default")]
        [InlineData("admin_user-6fe1b3d2-0d7c-4c1e-9d5a-2f3b8c9e4a10")]
        public async Task Test_Workspace_Commands_Accept_Valid_Names(string name)
        {
            var errors = await Validate(new Workspaces.Create.Command { Name = name, DirectoryId = Guid.NewGuid() });

            Assert.Empty(errors);
        }

        [Fact]
        public async Task Test_Workspace_Edit_Requires_Name()
        {
            var errors = await Validate(new Workspaces.Edit.Command { Id = Guid.NewGuid(), Name = null, DirectoryId = Guid.NewGuid() });

            Assert.True(errors.ContainsKey("Name"));
        }

        [Fact]
        public async Task Test_Workspace_PartialEdit_Allows_Missing_Name()
        {
            var errors = await Validate(new Workspaces.PartialEdit.Command { Id = Guid.NewGuid(), Name = null });

            Assert.Empty(errors);
        }

        private Task<Dictionary<string, string[]>> ValidateObject(object command)
        {
            var method = typeof(InterfaceValidationTest)
                .GetMethod(nameof(Validate), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .MakeGenericMethod(command.GetType());

            return (Task<Dictionary<string, string[]>>)method.Invoke(this, [command]);
        }

        private async Task<Dictionary<string, string[]>> Validate<T>(T command)
        {
            using var scope = _services.CreateScope();
            var behavior = new ValidationBehavior<T>(scope.ServiceProvider);

            try
            {
                await behavior.Process(command, default);
                return [];
            }
            catch (ApiValidationException ex)
            {
                return ex.Errors;
            }
        }
    }
}
