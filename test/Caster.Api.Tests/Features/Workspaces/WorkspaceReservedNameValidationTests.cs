// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Caster.Api.Features.Shared.Services;
using Caster.Api.Features.Workspaces;
using Caster.Api.Infrastructure.Options;
using NSubstitute;
using Xunit;

namespace Caster.Api.Tests.Features.Workspaces
{
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
            var validator = new Create.CommandValidator(_validationService, _options);
            var command = new Create.Command { Name = name, DirectoryId = Guid.NewGuid() };

            var result = await validator.ValidateAsync(command, TestContext.Current.CancellationToken);

            Assert.Contains(result.Errors, x => x.PropertyName == "Name");
        }

        [Fact]
        public async Task Test_Create_Allows_An_Ordinary_Workspace_Name()
        {
            var validator = new Create.CommandValidator(_validationService, _options);
            var command = new Create.Command { Name = "default", DirectoryId = Guid.NewGuid() };

            var result = await validator.ValidateAsync(command, TestContext.Current.CancellationToken);

            Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(x => x.ErrorMessage)));
        }

        [Theory]
        [InlineData("__Workspaces__")]
        [InlineData("__Caster__")]
        public async Task Test_Edit_Rejects_Reserved_Workspace_Names(string name)
        {
            var validator = new Edit.CommandValidator(_validationService, _options);
            var command = new Edit.Command { Id = Guid.NewGuid(), Name = name, DirectoryId = Guid.NewGuid() };

            var result = await validator.ValidateAsync(command, TestContext.Current.CancellationToken);

            Assert.Contains(result.Errors, x => x.PropertyName == "Name");
        }

        [Theory]
        [InlineData("__Workspaces__")]
        [InlineData("__Caster__")]
        [InlineData("..")]
        public async Task Test_PartialEdit_Rejects_Reserved_Workspace_Names(string name)
        {
            var validator = new PartialEdit.CommandValidator(_validationService, _options);
            var command = new PartialEdit.Command { Id = Guid.NewGuid(), Name = name };

            var result = await validator.ValidateAsync(command, TestContext.Current.CancellationToken);

            Assert.Contains(result.Errors, x => x.PropertyName == "Name");
        }

        [Theory]
        [InlineData("patched")]
        [InlineData(null)] // Name is optional on a partial edit
        public async Task Test_PartialEdit_Allows_An_Ordinary_Or_Absent_Workspace_Name(string name)
        {
            var validator = new PartialEdit.CommandValidator(_validationService, _options);
            var command = new PartialEdit.Command { Id = Guid.NewGuid(), Name = name };

            var result = await validator.ValidateAsync(command, TestContext.Current.CancellationToken);

            Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(x => x.ErrorMessage)));
        }
    }
}
