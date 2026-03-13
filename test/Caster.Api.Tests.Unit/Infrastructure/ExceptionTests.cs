// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using Caster.Api.Infrastructure.Exceptions;
using Xunit;

namespace Caster.Api.Tests.Unit.Infrastructure
{
    [Trait("Category", "Unit")]
    public class ExceptionTests
    {
        [Fact]
        public void GetStatusCode_WhenForbiddenException_ReturnsForbidden()
        {
            var exception = new ForbiddenException();

            Assert.Equal(HttpStatusCode.Forbidden, exception.GetStatusCode());
        }

        [Fact]
        public void Constructor_WhenForbiddenExceptionWithNoMessage_HasDefaultMessage()
        {
            var exception = new ForbiddenException();

            Assert.Equal("Insufficient Permissions", exception.Message);
        }

        [Fact]
        public void Constructor_WhenForbiddenExceptionWithCustomMessage_UsesCustomMessage()
        {
            var exception = new ForbiddenException("Custom forbidden message");

            Assert.Equal("Custom forbidden message", exception.Message);
        }

        [Fact]
        public void GetStatusCode_WhenEntityNotFoundException_ReturnsNotFound()
        {
            var exception = new EntityNotFoundException<Caster.Api.Features.Projects.Project>();

            Assert.Equal(HttpStatusCode.NotFound, exception.GetStatusCode());
        }

        [Fact]
        public void Constructor_WhenEntityNotFoundExceptionWithNoMessage_HasFormattedMessage()
        {
            var exception = new EntityNotFoundException<Caster.Api.Features.Projects.Project>();

            Assert.Contains("Project", exception.Message);
            Assert.Contains("not found", exception.Message);
        }

        [Fact]
        public void Constructor_WhenEntityNotFoundExceptionWithCustomMessage_UsesCustomMessage()
        {
            var exception = new EntityNotFoundException<Caster.Api.Features.Projects.Project>("Custom not found");

            Assert.Equal("Custom not found", exception.Message);
        }

        [Fact]
        public void Constructor_WhenEntityNotFoundExceptionWithCamelCaseType_InsertsSpaces()
        {
            var exception = new EntityNotFoundException<Domain.Models.ProjectMembership>();

            // Should contain spaces between words
            Assert.Contains("Project", exception.Message);
            Assert.Contains("Membership", exception.Message);
        }

        [Fact]
        public void Constructor_WhenFileConflictException_HasDefaultMessage()
        {
            var exception = new FileConflictException();

            Assert.Contains("lock", exception.Message);
        }

        [Fact]
        public void GetStatusCode_WhenFileConflictException_ReturnsConflict()
        {
            var exception = new FileConflictException();

            Assert.Equal(HttpStatusCode.Conflict, exception.GetStatusCode());
        }

        [Fact]
        public void Constructor_WhenFileAdminLockedException_HasCorrectMessage()
        {
            var exception = new FileAdminLockedException();

            Assert.Contains("Administrator", exception.Message);
        }

        [Fact]
        public void Constructor_WhenFileInsufficientPrivilegesException_HasCorrectMessage()
        {
            var exception = new FileInsufficientPrivilegesException();

            Assert.Contains("privileges", exception.Message);
        }
    }
}
