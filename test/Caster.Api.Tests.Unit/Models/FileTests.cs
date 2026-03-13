// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Exceptions;
using TUnit.Core;

namespace Caster.Api.Tests.Unit.Models
{
    [Category("Unit")]
    public class FileTests
    {
        private readonly Guid _userId = Guid.NewGuid();
        private readonly Guid _otherUserId = Guid.NewGuid();

        private File CreateFile()
        {
            var directory = new Directory { Id = Guid.NewGuid(), Name = "TestDir" };
            return new File { Name = "main.tf", Directory = directory };
        }

        #region Lock Tests

        [Test]
        public async Task Lock_WhenFileUnlocked_SetsLockedById()
        {
            var file = CreateFile();

            file.Lock(_userId, canLock: false);

            await Assert.That(file.LockedById).IsEqualTo(_userId);
        }

        [Test]
        public async Task Lock_WhenLockedBySameUser_Succeeds()
        {
            var file = CreateFile();
            file.Lock(_userId, canLock: false);

            file.Lock(_userId, canLock: false);

            await Assert.That(file.LockedById).IsEqualTo(_userId);
        }

        [Test]
        public async Task Lock_WhenLockedByDifferentUser_ThrowsFileConflictException()
        {
            var file = CreateFile();
            file.Lock(_userId, canLock: false);

            await Assert.That(() => file.Lock(_otherUserId, canLock: false))
                .ThrowsExactly<FileConflictException>();
        }

        [Test]
        public async Task Lock_WhenAdminLockedAndUserCannotLock_ThrowsFileAdminLockedException()
        {
            var file = CreateFile();
            file.AdministrativelyLock(canLock: true);

            await Assert.That(() => file.Lock(_userId, canLock: false))
                .ThrowsExactly<FileAdminLockedException>();
        }

        [Test]
        public async Task Lock_WhenAdminLockedAndUserCanLock_Succeeds()
        {
            var file = CreateFile();
            file.AdministrativelyLock(canLock: true);

            file.Lock(_userId, canLock: true);

            await Assert.That(file.LockedById).IsEqualTo(_userId);
        }

        #endregion

        #region Unlock Tests

        [Test]
        public async Task Unlock_WhenLockedBySameUser_ClearsLock()
        {
            var file = CreateFile();
            file.Lock(_userId, canLock: false);

            file.Unlock(_userId);

            await Assert.That(file.LockedById).IsNull();
        }

        [Test]
        public async Task Unlock_WhenLockedByDifferentUser_ThrowsFileConflictException()
        {
            var file = CreateFile();
            file.Lock(_userId, canLock: false);

            await Assert.That(() => file.Unlock(_otherUserId))
                .ThrowsExactly<FileConflictException>();
        }

        [Test]
        public async Task Unlock_WhenFileNotLocked_ThrowsFileConflictException()
        {
            var file = CreateFile();

            await Assert.That(() => file.Unlock(_userId))
                .ThrowsExactly<FileConflictException>();
        }

        #endregion

        #region Save Tests

        [Test]
        public async Task Save_WhenLockedBySameUser_UpdatesModifiedFields()
        {
            var file = CreateFile();
            file.Lock(_userId, canLock: false);

            file.Save(_userId, canLock: false);

            await Assert.That(file.ModifiedById).IsEqualTo(_userId);
            await Assert.That(file.DateSaved).IsNotNull();
            await Assert.That(file.FileVersions).HasSingleItem();
        }

        [Test]
        public async Task Save_WhenLockedByDifferentUser_ThrowsFileConflictException()
        {
            var file = CreateFile();
            file.Lock(_userId, canLock: false);

            await Assert.That(() => file.Save(_otherUserId, canLock: false))
                .ThrowsExactly<FileConflictException>();
        }

        [Test]
        public async Task Save_WhenFileNotLocked_ThrowsFileConflictException()
        {
            var file = CreateFile();

            await Assert.That(() => file.Save(_userId, canLock: false))
                .ThrowsExactly<FileConflictException>();
        }

        [Test]
        public async Task Save_WhenBypassLockTrue_SucceedsEvenWhenNotLocked()
        {
            var file = CreateFile();

            file.Save(_userId, canLock: false, bypassLock: true);

            await Assert.That(file.ModifiedById).IsEqualTo(_userId);
            await Assert.That(file.DateSaved).IsNotNull();
        }

        [Test]
        public async Task Save_WhenAdminLockedAndUserCannotLock_ThrowsFileAdminLockedException()
        {
            var file = CreateFile();
            file.AdministrativelyLock(canLock: true);
            file.Lock(_userId, canLock: true);

            await Assert.That(() => file.Save(_userId, canLock: false))
                .ThrowsExactly<FileAdminLockedException>();
        }

        [Test]
        public async Task Save_WhenCalledMultipleTimes_CreatesFileVersionOnEachSave()
        {
            var file = CreateFile();
            file.Lock(_userId, canLock: false);

            file.Save(_userId, canLock: false);
            file.Save(_userId, canLock: false);

            await Assert.That(file.FileVersions.Count).IsEqualTo(2);
        }

        #endregion

        #region Delete Tests

        [Test]
        public async Task Delete_WhenCalled_SetsIsDeleted()
        {
            var file = CreateFile();

            file.Delete(canLock: false);

            await Assert.That(file.IsDeleted).IsTrue();
        }

        [Test]
        public async Task Delete_WhenAdminLockedAndUserCannotLock_ThrowsFileAdminLockedException()
        {
            var file = CreateFile();
            file.AdministrativelyLock(canLock: true);

            await Assert.That(() => file.Delete(canLock: false))
                .ThrowsExactly<FileAdminLockedException>();
        }

        [Test]
        public async Task Delete_WhenAdminLockedAndUserCanLock_Succeeds()
        {
            var file = CreateFile();
            file.AdministrativelyLock(canLock: true);

            file.Delete(canLock: true);

            await Assert.That(file.IsDeleted).IsTrue();
        }

        #endregion

        #region Administrative Lock Tests

        [Test]
        public async Task AdministrativelyLock_WhenUserCanLock_SetsAdminLocked()
        {
            var file = CreateFile();

            file.AdministrativelyLock(canLock: true);

            await Assert.That(file.AdministrativelyLocked).IsTrue();
        }

        [Test]
        public async Task AdministrativelyLock_WhenUserCannotLock_ThrowsFileInsufficientPrivilegesException()
        {
            var file = CreateFile();

            await Assert.That(() => file.AdministrativelyLock(canLock: false))
                .ThrowsExactly<FileInsufficientPrivilegesException>();
        }

        [Test]
        public async Task AdministrativelyUnlock_WhenUserCanLock_ClearsAdminLock()
        {
            var file = CreateFile();
            file.AdministrativelyLock(canLock: true);

            file.AdministrativelyUnlock(canLock: true);

            await Assert.That(file.AdministrativelyLocked).IsFalse();
        }

        [Test]
        public async Task AdministrativelyUnlock_WhenUserCannotLock_ThrowsFileInsufficientPrivilegesException()
        {
            var file = CreateFile();
            file.AdministrativelyLock(canLock: true);

            await Assert.That(() => file.AdministrativelyUnlock(canLock: false))
                .ThrowsExactly<FileInsufficientPrivilegesException>();
        }

        #endregion

        #region CanLock Tests

        [Test]
        public async Task CanLock_WhenFileUnlocked_ReturnsTrue()
        {
            var file = CreateFile();

            await Assert.That(file.CanLock(_userId, canLock: false)).IsTrue();
        }

        [Test]
        public async Task CanLock_WhenLockedBySameUser_ReturnsTrue()
        {
            var file = CreateFile();
            file.Lock(_userId, canLock: false);

            await Assert.That(file.CanLock(_userId, canLock: false)).IsTrue();
        }

        [Test]
        public async Task CanLock_WhenLockedByDifferentUser_ReturnsFalse()
        {
            var file = CreateFile();
            file.Lock(_userId, canLock: false);

            await Assert.That(file.CanLock(_otherUserId, canLock: false)).IsFalse();
        }

        [Test]
        public async Task CanLock_WhenAdminLockedAndUserCannotLock_ReturnsFalse()
        {
            var file = CreateFile();
            file.AdministrativelyLock(canLock: true);

            await Assert.That(file.CanLock(_userId, canLock: false)).IsFalse();
        }

        [Test]
        public async Task CanLock_WhenAdminLockedAndUserCanLock_ReturnsTrue()
        {
            var file = CreateFile();
            file.AdministrativelyLock(canLock: true);

            await Assert.That(file.CanLock(_userId, canLock: true)).IsTrue();
        }

        #endregion

        #region Tag Tests

        [Test]
        public async Task Tag_WithTagName_CreatesFileVersionWithTag()
        {
            var file = CreateFile();
            var tag = "v1.0";
            var dateTagged = DateTime.UtcNow;

            file.Tag(tag, _userId, dateTagged);

            await Assert.That(file.FileVersions).HasSingleItem();
        }

        #endregion
    }
}
