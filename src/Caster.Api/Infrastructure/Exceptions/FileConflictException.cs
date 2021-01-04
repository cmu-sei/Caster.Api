// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Caster.Api.Infrastructure.Exceptions
{
    public class FileConflictException : ConflictException
    {
        public FileConflictException()
            : base("You cannot make changes to a File without holding it's lock")
        {
        }
    }

    public class FileAdminLockedException : ConflictException
    {
        public FileAdminLockedException()
            : base("This File has been locked by an Administrator. It must be unlocked before you can make changes")
        {
        }
    }

    public class FileInsufficientPrivilegesException : ConflictException
    {
        public FileInsufficientPrivilegesException()
            : base("You do not have sufficient privileges to perform this file action.")
        {
        }
    }
}
