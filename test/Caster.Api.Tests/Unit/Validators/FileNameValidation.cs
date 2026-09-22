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
        [InlineData("subdir/main.tf")]
        [InlineData("..\\escaped.tf")]
        [InlineData("main.tf\0.txt")]
        [InlineData("..")]
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
        [InlineData("my file.tf")] // spaces were accepted before this rule existed
        public async Task Test_Create_Allows_Valid_File_Names(string name)
        {
            var validator = new Create.CommandValidator(_validationService);
            var command = new Create.Command() { Name = name, DirectoryId = Guid.NewGuid() };

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
