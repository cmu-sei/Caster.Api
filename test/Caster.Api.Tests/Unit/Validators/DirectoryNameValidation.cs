// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Caster.Api.Features.Directories;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Infrastructure.Options;
using NSubstitute;
using Xunit;

namespace Caster.Api.Tests.Unit.Validators
{
    [Trait("Category", "Unit")]
    [Trait("Category", "Directory")]
    public class DirectoryNameValidationTest
    {
        private readonly IValidationService _validationService;
        private readonly TerraformOptions _options = new() { MaxParallelism = 25 };

        public DirectoryNameValidationTest()
        {
            _validationService = Substitute.For<IValidationService>();
            _validationService.DirectoryExists(Arg.Any<Guid>()).Returns(Task.FromResult(true));
            _validationService.ProjectExists(Arg.Any<Guid>()).Returns(Task.FromResult(true));
        }

        public static TheoryData<string> InvalidNames =>
        [
            // Reserved by the archive format
            "__Workspaces__",
            "__workspaces__",
            "__Caster__",
            // Would put traversal or extra segments into an archive entry path
            "..",
            ".",
            "../../etc",
            "a/b",
            "a\\b",
            "/",
            // Outside of the allowlist
            "my directory",
            "name(1)",
            "café",
            "name\0",
            "name\n",
            "",
            null
        ];

        public static TheoryData<string> ValidNames =>
        [
            "Infrastructure",
            "my-dir_1.2",
            "__Workspaces__x",
            "0",
            "a.b.c"
        ];

        [Theory]
        [MemberData(nameof(InvalidNames))]
        public async Task Test_Create_Rejects_Invalid_Directory_Names(string name)
        {
            var validator = new Create.CommandValidator(_validationService, _options);
            var command = new Create.Command { Name = name, ProjectId = Guid.NewGuid() };

            var result = await validator.ValidateAsync(command);

            Assert.Contains(result.Errors, x => x.PropertyName == nameof(Create.Command.Name));
        }

        [Theory]
        [MemberData(nameof(ValidNames))]
        public async Task Test_Create_Allows_Valid_Directory_Names(string name)
        {
            var validator = new Create.CommandValidator(_validationService, _options);
            var command = new Create.Command { Name = name, ProjectId = Guid.NewGuid() };

            var result = await validator.ValidateAsync(command);

            Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(x => x.ErrorMessage)));
        }

        [Theory]
        [InlineData(90, true)]
        [InlineData(91, false)]
        public async Task Test_Create_Limits_The_Length_Of_Directory_Names(int length, bool expectedValid)
        {
            var validator = new Create.CommandValidator(_validationService, _options);
            var command = new Create.Command { Name = new string('a', length), ProjectId = Guid.NewGuid() };

            var result = await validator.ValidateAsync(command);

            Assert.Equal(expectedValid, result.IsValid);
        }

        [Theory]
        [MemberData(nameof(InvalidNames))]
        public async Task Test_Edit_Rejects_Invalid_Directory_Names(string name)
        {
            var validator = new Edit.CommandValidator(_validationService, _options);
            var command = new Edit.Command { Id = Guid.NewGuid(), Name = name };

            var result = await validator.ValidateAsync(command);

            Assert.Contains(result.Errors, x => x.PropertyName == nameof(Edit.Command.Name));
        }

        [Fact]
        public async Task Test_Edit_Allows_Valid_Directory_Names()
        {
            var validator = new Edit.CommandValidator(_validationService, _options);
            var command = new Edit.Command { Id = Guid.NewGuid(), Name = "Infrastructure" };

            var result = await validator.ValidateAsync(command);

            Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(x => x.ErrorMessage)));
        }

        [Theory]
        [InlineData("__Workspaces__")]
        [InlineData("__Caster__")]
        [InlineData("..")]
        [InlineData("a/b")]
        [InlineData("")]
        public async Task Test_PartialEdit_Rejects_Invalid_Directory_Names(string name)
        {
            var validator = new PartialEdit.CommandValidator(_validationService, _options);
            var command = new PartialEdit.Command { Id = Guid.NewGuid(), Name = name };

            var result = await validator.ValidateAsync(command);

            Assert.Contains(result.Errors, x => x.PropertyName == nameof(PartialEdit.Command.Name));
        }

        [Theory]
        [InlineData("Infrastructure")]
        [InlineData(null)] // Name is optional on a partial edit
        public async Task Test_PartialEdit_Allows_Valid_Directory_Names(string name)
        {
            var validator = new PartialEdit.CommandValidator(_validationService, _options);
            var command = new PartialEdit.Command { Id = Guid.NewGuid(), Name = name };

            var result = await validator.ValidateAsync(command);

            Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(x => x.ErrorMessage)));
        }
    }

    [Trait("Category", "Unit")]
    [Trait("Category", "Workspace")]
    public class WorkspaceReservedNameValidationTest
    {
        private readonly IValidationService _validationService;
        private readonly TerraformOptions _options = new() { MaxParallelism = 25 };

        public WorkspaceReservedNameValidationTest()
        {
            _validationService = Substitute.For<IValidationService>();
            _validationService.DirectoryExists(Arg.Any<Guid>()).Returns(Task.FromResult(true));
        }

        [Theory]
        [InlineData("__Workspaces__")]
        [InlineData("__caster__")]
        [InlineData("..")]
        [InlineData(".")]
        public async Task Test_Create_Rejects_Reserved_Workspace_Names(string name)
        {
            var validator = new Caster.Api.Features.Workspaces.Create.CommandValidator(_validationService, _options);
            var command = new Caster.Api.Features.Workspaces.Create.Command { Name = name, DirectoryId = Guid.NewGuid() };

            var result = await validator.ValidateAsync(command);

            Assert.Contains(result.Errors, x => x.PropertyName == "Name");
        }

        [Fact]
        public async Task Test_Create_Allows_An_Ordinary_Workspace_Name()
        {
            var validator = new Caster.Api.Features.Workspaces.Create.CommandValidator(_validationService, _options);
            var command = new Caster.Api.Features.Workspaces.Create.Command { Name = "default", DirectoryId = Guid.NewGuid() };

            var result = await validator.ValidateAsync(command);

            Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(x => x.ErrorMessage)));
        }

        [Theory]
        [InlineData("__Workspaces__")]
        [InlineData("__Caster__")]
        public async Task Test_Edit_Rejects_Reserved_Workspace_Names(string name)
        {
            var validator = new Caster.Api.Features.Workspaces.Edit.CommandValidator(_validationService, _options);
            var command = new Caster.Api.Features.Workspaces.Edit.Command { Id = Guid.NewGuid(), Name = name, DirectoryId = Guid.NewGuid() };

            var result = await validator.ValidateAsync(command);

            Assert.Contains(result.Errors, x => x.PropertyName == "Name");
        }
    }
}
