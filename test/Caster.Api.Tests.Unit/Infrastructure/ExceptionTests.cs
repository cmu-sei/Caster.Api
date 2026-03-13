// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using Caster.Api.Infrastructure.Exceptions;
using TUnit.Core;

namespace Caster.Api.Tests.Unit.Infrastructure
{
    [Category("Unit")]
    public class ExceptionTests
    {
        [Test]
        public async Task GetStatusCode_WhenForbiddenException_ReturnsForbidden()
        {
            var exception = new ForbiddenException();

            await Assert.That(exception.GetStatusCode()).IsEqualTo(HttpStatusCode.Forbidden);
        }

        [Test]
        public async Task Constructor_WhenForbiddenExceptionWithNoMessage_HasDefaultMessage()
        {
            var exception = new ForbiddenException();

            await Assert.That(exception.Message).IsEqualTo("Insufficient Permissions");
        }

        [Test]
        public async Task Constructor_WhenForbiddenExceptionWithCustomMessage_UsesCustomMessage()
        {
            var exception = new ForbiddenException("Custom forbidden message");

            await Assert.That(exception.Message).IsEqualTo("Custom forbidden message");
        }

        [Test]
        public async Task GetStatusCode_WhenEntityNotFoundException_ReturnsNotFound()
        {
            var exception = new EntityNotFoundException<Caster.Api.Features.Projects.Project>();

            await Assert.That(exception.GetStatusCode()).IsEqualTo(HttpStatusCode.NotFound);
        }

        [Test]
        public async Task Constructor_WhenEntityNotFoundExceptionWithNoMessage_HasFormattedMessage()
        {
            var exception = new EntityNotFoundException<Caster.Api.Features.Projects.Project>();

            await Assert.That(exception.Message).Contains("Project");
            await Assert.That(exception.Message).Contains("not found");
        }

        [Test]
        public async Task Constructor_WhenEntityNotFoundExceptionWithCustomMessage_UsesCustomMessage()
        {
            var exception = new EntityNotFoundException<Caster.Api.Features.Projects.Project>("Custom not found");

            await Assert.That(exception.Message).IsEqualTo("Custom not found");
        }

        [Test]
        public async Task Constructor_WhenEntityNotFoundExceptionWithCamelCaseType_InsertsSpaces()
        {
            var exception = new EntityNotFoundException<Domain.Models.ProjectMembership>();

            // Should contain spaces between words
            await Assert.That(exception.Message).Contains("Project");
            await Assert.That(exception.Message).Contains("Membership");
        }

        [Test]
        public async Task Constructor_WhenFileConflictException_HasDefaultMessage()
        {
            var exception = new FileConflictException();

            await Assert.That(exception.Message).Contains("lock");
        }

        [Test]
        public async Task GetStatusCode_WhenFileConflictException_ReturnsConflict()
        {
            var exception = new FileConflictException();

            await Assert.That(exception.GetStatusCode()).IsEqualTo(HttpStatusCode.Conflict);
        }

        [Test]
        public async Task Constructor_WhenFileAdminLockedException_HasCorrectMessage()
        {
            var exception = new FileAdminLockedException();

            await Assert.That(exception.Message).Contains("Administrator");
        }

        [Test]
        public async Task Constructor_WhenFileInsufficientPrivilegesException_HasCorrectMessage()
        {
            var exception = new FileInsufficientPrivilegesException();

            await Assert.That(exception.Message).Contains("privileges");
        }
    }
}
