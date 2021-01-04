// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;

namespace Caster.Api.Domain.Models
{
    public enum ArchiveType
    {
        zip,
        tgz
    }

    public static class ArchiveTypeHelpers
    {
        public static string GetContentType(this ArchiveType archiveType)
        {
            switch(archiveType)
            {
                case ArchiveType.zip:
                    return "application/zip";
                case ArchiveType.tgz:
                    return "application/gzip";
                default:
                    throw new ArgumentException();
            }
        }

        public static string GetExtension(this ArchiveType archiveType)
        {
            switch(archiveType)
            {
                case ArchiveType.zip:
                    return "zip";
                case ArchiveType.tgz:
                    return "tar.gz";
                default:
                    throw new ArgumentException();
            }
        }

        public static string[] GetValidExtensions()
        {
            return new string[] { ".zip", ".tar.gz", ".tgz" };
        }

        public static ArchiveType GetType(string filename)
        {
            if (filename.ToLower().EndsWith(".zip"))
            {
                return ArchiveType.zip;
            }
            else if (filename.ToLower().EndsWith(".tar.gz") || filename.ToLower().EndsWith(".tgz"))
            {
                return ArchiveType.tgz;
            }
            else
            {
                throw new ArgumentException();
            }
        }
    }
}
