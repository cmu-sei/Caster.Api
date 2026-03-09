// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Caster.Api.Domain.Models;
using Caster.Api.Infrastructure.Exceptions;
using Xunit;

namespace Caster.Api.Tests.Unit.Models
{
    [Trait("Category", "Unit")]
    [Trait("Category", "File")]
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

        [Fact]
        public void Lock_WhenUnlocked_SetsLockedById()
        {
            var file = CreateFile();

            file.Lock(_userId, canLock: false);

            Assert.Equal(_userId, file.LockedById);
        }

        [Fact]
        public void Lock_WhenLockedBySameUser_Succeeds()
        {
            var file = CreateFile();
            file.Lock(_userId, canLock: false);

            file.Lock(_userId, canLock: false);

            Assert.Equal(_userId, file.LockedById);
        }

        [Fact]
        public void Lock_WhenLockedByOtherUser_ThrowsFileConflictException()
        {
            var file = CreateFile();
            file.Lock(_userId, canLock: false);

            Assert.Throws<FileConflictException>(() => file.Lock(_otherUserId, canLock: false));
        }

        [Fact]
        public void Lock_WhenAdminLockedAndNoCanLock_ThrowsFileAdminLockedException()
        {
            var file = CreateFile();
            file.AdministrativelyLock(canLock: true);

            Assert.Throws<FileAdminLockedException>(() => file.Lock(_userId, canLock: false));
        }

        [Fact]
        public void Lock_WhenAdminLockedAndCanLock_Succeeds()
        {
            var file = CreateFile();
            file.AdministrativelyLock(canLock: true);

            file.Lock(_userId, canLock: true);

            Assert.Equal(_userId, file.LockedById);
        }

        #endregion

        #region Unlock Tests

        [Fact]
        public void Unlock_WhenLockedBySameUser_ClearsLock()
        {
            var file = CreateFile();
            file.Lock(_userId, canLock: false);

            file.Unlock(_userId);

            Assert.Null(file.LockedById);
        }

        [Fact]
        public void Unlock_WhenLockedByOtherUser_ThrowsFileConflictException()
        {
            var file = CreateFile();
            file.Lock(_userId, canLock: false);

            Assert.Throws<FileConflictException>(() => file.Unlock(_otherUserId));
        }

        [Fact]
        public void Unlock_WhenNotLocked_ThrowsFileConflictException()
        {
            var file = CreateFile();

            Assert.Throws<FileConflictException>(() => file.Unlock(_userId));
        }

        #endregion

        #region Save Tests

        [Fact]
        public void Save_WhenLockedBySameUser_UpdatesModifiedFields()
        {
            var file = CreateFile();
            file.Lock(_userId, canLock: false);

            file.Save(_userId, canLock: false);

            Assert.Equal(_userId, file.ModifiedById);
            Assert.NotNull(file.DateSaved);
            Assert.Single(file.FileVersions);
        }

        [Fact]
        public void Save_WhenLockedByOtherUser_ThrowsFileConflictException()
        {
            var file = CreateFile();
            file.Lock(_userId, canLock: false);

            Assert.Throws<FileConflictException>(() => file.Save(_otherUserId, canLock: false));
        }

        [Fact]
        public void Save_WhenNotLocked_ThrowsFileConflictException()
        {
            var file = CreateFile();

            Assert.Throws<FileConflictException>(() => file.Save(_userId, canLock: false));
        }

        [Fact]
        public void Save_WithBypassLock_SucceedsEvenWhenNotLocked()
        {
            var file = CreateFile();

            file.Save(_userId, canLock: false, bypassLock: true);

            Assert.Equal(_userId, file.ModifiedById);
            Assert.NotNull(file.DateSaved);
        }

        [Fact]
        public void Save_WhenAdminLockedAndNoCanLock_ThrowsFileAdminLockedException()
        {
            var file = CreateFile();
            file.AdministrativelyLock(canLock: true);
            file.Lock(_userId, canLock: true);

            Assert.Throws<FileAdminLockedException>(() => file.Save(_userId, canLock: false));
        }

        [Fact]
        public void Save_CreatesFileVersionOnEachSave()
        {
            var file = CreateFile();
            file.Lock(_userId, canLock: false);

            file.Save(_userId, canLock: false);
            file.Save(_userId, canLock: false);

            Assert.Equal(2, file.FileVersions.Count);
        }

        #endregion

        #region Delete Tests

        [Fact]
        public void Delete_SetsIsDeleted()
        {
            var file = CreateFile();

            file.Delete(canLock: false);

            Assert.True(file.IsDeleted);
        }

        [Fact]
        public void Delete_WhenAdminLockedAndNoCanLock_ThrowsFileAdminLockedException()
        {
            var file = CreateFile();
            file.AdministrativelyLock(canLock: true);

            Assert.Throws<FileAdminLockedException>(() => file.Delete(canLock: false));
        }

        [Fact]
        public void Delete_WhenAdminLockedAndCanLock_Succeeds()
        {
            var file = CreateFile();
            file.AdministrativelyLock(canLock: true);

            file.Delete(canLock: true);

            Assert.True(file.IsDeleted);
        }

        #endregion

        #region Administrative Lock Tests

        [Fact]
        public void AdministrativelyLock_WithCanLock_SetsAdminLocked()
        {
            var file = CreateFile();

            file.AdministrativelyLock(canLock: true);

            Assert.True(file.AdministrativelyLocked);
        }

        [Fact]
        public void AdministrativelyLock_WithoutCanLock_ThrowsFileInsufficientPrivilegesException()
        {
            var file = CreateFile();

            Assert.Throws<FileInsufficientPrivilegesException>(() => file.AdministrativelyLock(canLock: false));
        }

        [Fact]
        public void AdministrativelyUnlock_WithCanLock_ClearsAdminLock()
        {
            var file = CreateFile();
            file.AdministrativelyLock(canLock: true);

            file.AdministrativelyUnlock(canLock: true);

            Assert.False(file.AdministrativelyLocked);
        }

        [Fact]
        public void AdministrativelyUnlock_WithoutCanLock_ThrowsFileInsufficientPrivilegesException()
        {
            var file = CreateFile();
            file.AdministrativelyLock(canLock: true);

            Assert.Throws<FileInsufficientPrivilegesException>(() => file.AdministrativelyUnlock(canLock: false));
        }

        #endregion

        #region CanLock Tests

        [Fact]
        public void CanLock_WhenUnlocked_ReturnsTrue()
        {
            var file = CreateFile();

            Assert.True(file.CanLock(_userId, canLock: false));
        }

        [Fact]
        public void CanLock_WhenLockedBySameUser_ReturnsTrue()
        {
            var file = CreateFile();
            file.Lock(_userId, canLock: false);

            Assert.True(file.CanLock(_userId, canLock: false));
        }

        [Fact]
        public void CanLock_WhenLockedByOtherUser_ReturnsFalse()
        {
            var file = CreateFile();
            file.Lock(_userId, canLock: false);

            Assert.False(file.CanLock(_otherUserId, canLock: false));
        }

        [Fact]
        public void CanLock_WhenAdminLockedAndNoCanLock_ReturnsFalse()
        {
            var file = CreateFile();
            file.AdministrativelyLock(canLock: true);

            Assert.False(file.CanLock(_userId, canLock: false));
        }

        [Fact]
        public void CanLock_WhenAdminLockedAndCanLock_ReturnsTrue()
        {
            var file = CreateFile();
            file.AdministrativelyLock(canLock: true);

            Assert.True(file.CanLock(_userId, canLock: true));
        }

        #endregion

        #region Tag Tests

        [Fact]
        public void Tag_CreatesFileVersionWithTag()
        {
            var file = CreateFile();
            var tag = "v1.0";
            var dateTagged = DateTime.UtcNow;

            file.Tag(tag, _userId, dateTagged);

            Assert.Single(file.FileVersions);
        }

        #endregion
    }
}
