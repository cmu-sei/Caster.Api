// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using Caster.Api.Infrastructure.Exceptions;
using Xunit;

namespace Caster.Api.Tests.Unit.Infrastructure
{
    [Trait("Category", "Unit")]
    [Trait("Category", "Exceptions")]
    public class ExceptionTests
    {
        [Fact]
        public void ForbiddenException_HasCorrectStatusCode()
        {
            var exception = new ForbiddenException();

            Assert.Equal(HttpStatusCode.Forbidden, exception.GetStatusCode());
        }

        [Fact]
        public void ForbiddenException_HasDefaultMessage()
        {
            var exception = new ForbiddenException();

            Assert.Equal("Insufficient Permissions", exception.Message);
        }

        [Fact]
        public void ForbiddenException_WithCustomMessage_UsesCustomMessage()
        {
            var exception = new ForbiddenException("Custom forbidden message");

            Assert.Equal("Custom forbidden message", exception.Message);
        }

        [Fact]
        public void EntityNotFoundException_HasCorrectStatusCode()
        {
            var exception = new EntityNotFoundException<Caster.Api.Features.Projects.Project>();

            Assert.Equal(HttpStatusCode.NotFound, exception.GetStatusCode());
        }

        [Fact]
        public void EntityNotFoundException_HasFormattedMessage()
        {
            var exception = new EntityNotFoundException<Caster.Api.Features.Projects.Project>();

            Assert.Contains("Project", exception.Message);
            Assert.Contains("not found", exception.Message);
        }

        [Fact]
        public void EntityNotFoundException_WithCustomMessage_UsesCustomMessage()
        {
            var exception = new EntityNotFoundException<Caster.Api.Features.Projects.Project>("Custom not found");

            Assert.Equal("Custom not found", exception.Message);
        }

        [Fact]
        public void EntityNotFoundException_WithCamelCaseType_InsertsSpaces()
        {
            var exception = new EntityNotFoundException<Domain.Models.ProjectMembership>();

            // Should contain spaces between words
            Assert.Contains("Project", exception.Message);
            Assert.Contains("Membership", exception.Message);
        }

        [Fact]
        public void FileConflictException_HasDefaultMessage()
        {
            var exception = new FileConflictException();

            Assert.Contains("lock", exception.Message);
        }

        [Fact]
        public void FileConflictException_HasConflictStatusCode()
        {
            var exception = new FileConflictException();

            Assert.Equal(HttpStatusCode.Conflict, exception.GetStatusCode());
        }

        [Fact]
        public void FileAdminLockedException_HasCorrectMessage()
        {
            var exception = new FileAdminLockedException();

            Assert.Contains("Administrator", exception.Message);
        }

        [Fact]
        public void FileInsufficientPrivilegesException_HasCorrectMessage()
        {
            var exception = new FileInsufficientPrivilegesException();

            Assert.Contains("privileges", exception.Message);
        }
    }
}
