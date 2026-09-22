// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Caster.Api.Features.Files;
using Caster.Api.Features.Shared.Services;
using NSubstitute;
using Xunit;

namespace Caster.Api.Tests.Unit.Validators
{
    [Trait("Category", "Unit")]
    [Trait("Category", "File")]
    public class FileNameValidationTest
    {
        private readonly IValidationService _validationService;

        public FileNameValidationTest()
        {
            _validationService = Substitute.For<IValidationService>();
            _validationService.DirectoryExists(Arg.Any<Guid>()).Returns(Task.FromResult(true));
            _validationService.WorkspaceExists(Arg.Any<Guid>()).Returns(Task.FromResult(true));
        }

        [Theory]
        [InlineData("../../etc/cron.d/backdoor")]
        [InlineData("../escaped.tf")]
        [InlineData("....//escaped.tf")]
        [InlineData("subdir/main.tf")]
        [InlineData("/etc/cron.d/backdoor")]
        [InlineData("..\\escaped.tf")]
        [InlineData("\\\\server\\share\\backdoor")]
        [InlineData("main.tf\0.txt")]
        [InlineData("main\tsomething.tf")]
        [InlineData("main\nsomething.tf")]
        [InlineData(".")]
        [InlineData("..")]
        [InlineData(" main.tf")]
        [InlineData("main.tf ")]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(null)]
        public async Task Test_Create_Rejects_Invalid_File_Names(string name)
        {
            var validator = new Create.CommandValidator(_validationService);
            var command = new Create.Command() { Name = name, DirectoryId = Guid.NewGuid() };

            var result = await validator.ValidateAsync(command);

            Assert.Contains(result.Errors, x => x.PropertyName == nameof(Create.Command.Name));
        }

        [Theory]
        [InlineData("main.tf")]
        [InlineData("variables.auto.tfvars.json")]
        [InlineData("my-file_2.tf")]
        // Anything that cannot escape the Workspace directory was accepted before this rule existed
        [InlineData("my file.tf")]
        [InlineData("network (old).tf")]
        [InlineData("a+b,c=d&e.tf")]
        [InlineData("café.tf")]
        // Percent encoded separators are never decoded, so they are stored and written literally
        [InlineData("%2e%2e%2fescaped.tf")]
        public async Task Test_Create_Allows_Valid_File_Names(string name)
        {
            var validator = new Create.CommandValidator(_validationService);
            var command = new Create.Command() { Name = name, DirectoryId = Guid.NewGuid() };

            var result = await validator.ValidateAsync(command);

            Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(x => x.ErrorMessage)));
        }

        [Theory]
        [InlineData(255, true)]
        [InlineData(256, false)]
        public async Task Test_Create_Limits_The_Length_Of_File_Names(int length, bool expectedValid)
        {
            var validator = new Create.CommandValidator(_validationService);
            var command = new Create.Command()
            {
                Name = new string('a', length - 3) + ".tf",
                DirectoryId = Guid.NewGuid()
            };

            var result = await validator.ValidateAsync(command);

            Assert.Equal(expectedValid, result.IsValid);
        }

        [Theory]
        [InlineData("../../etc/cron.d/backdoor")]
        [InlineData("../escaped.tf")]
        [InlineData("subdir/main.tf")]
        [InlineData("..")]
        [InlineData("")]
        [InlineData(null)]
        public async Task Test_Edit_Rejects_Invalid_File_Names(string name)
        {
            var validator = new Edit.CommandValidator(_validationService);
            var command = new Edit.Command() { Id = Guid.NewGuid(), Name = name, DirectoryId = Guid.NewGuid() };

            var result = await validator.ValidateAsync(command);

            Assert.Contains(result.Errors, x => x.PropertyName == nameof(Edit.Command.Name));
        }

        [Fact]
        public async Task Test_Edit_Allows_Valid_File_Names()
        {
            var validator = new Edit.CommandValidator(_validationService);
            var command = new Edit.Command() { Id = Guid.NewGuid(), Name = "main.tf", DirectoryId = Guid.NewGuid() };

            var result = await validator.ValidateAsync(command);

            Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(x => x.ErrorMessage)));
        }

        [Theory]
        [InlineData("../../etc/cron.d/backdoor")]
        [InlineData("subdir/main.tf")]
        [InlineData("..")]
        [InlineData("")]
        public async Task Test_PartialEdit_Rejects_Invalid_File_Names(string name)
        {
            var validator = new PartialEdit.CommandValidator(_validationService);
            var command = new PartialEdit.Command() { Id = Guid.NewGuid(), Name = name };

            var result = await validator.ValidateAsync(command);

            Assert.Contains(result.Errors, x => x.PropertyName == nameof(PartialEdit.Command.Name));
        }

        [Theory]
        [InlineData("main.tf")]
        [InlineData(null)] // Name is optional on a partial edit
        public async Task Test_PartialEdit_Allows_Valid_File_Names(string name)
        {
            var validator = new PartialEdit.CommandValidator(_validationService);
            var command = new PartialEdit.Command() { Id = Guid.NewGuid(), Name = name };

            var result = await validator.ValidateAsync(command);

            Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(x => x.ErrorMessage)));
        }

        [Theory]
        [InlineData("../../etc/cron.d/backdoor")]
        [InlineData("subdir/main.tf")]
        [InlineData("..")]
        [InlineData(null)]
        public async Task Test_Rename_Rejects_Invalid_File_Names(string name)
        {
            var validator = new Rename.CommandValidator();
            var command = new Rename.Command() { Id = Guid.NewGuid(), Name = name };

            var result = await validator.ValidateAsync(command);

            Assert.Contains(result.Errors, x => x.PropertyName == nameof(Rename.Command.Name));
        }

        [Fact]
        public async Task Test_Rename_Allows_Valid_File_Names()
        {
            var validator = new Rename.CommandValidator();
            var command = new Rename.Command() { Id = Guid.NewGuid(), Name = "main.tf" };

            var result = await validator.ValidateAsync(command);

            Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(x => x.ErrorMessage)));
        }
    }
}
