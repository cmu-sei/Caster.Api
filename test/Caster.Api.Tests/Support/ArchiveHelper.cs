// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;

namespace Caster.Api.Tests.Support;

/// <summary>Builds the zip archives the project and directory import endpoints read.</summary>
public static class ArchiveHelper
{
    /// <summary>
    /// A multipart form holding a zip with one file at <paramref name="entryPath"/> (relative to the imported
    /// directory, or prefixed with a directory name for a project), under the form field the endpoints bind.
    /// </summary>
    public static MultipartFormDataContent Zip(string entryPath, string content)
    {
        using var buffer = new MemoryStream();

        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry(entryPath).Open(), Encoding.ASCII))
        {
            writer.Write(content);
        }

        return new MultipartFormDataContent { { new ByteArrayContent(buffer.ToArray()), "archive", "import.zip" } };
    }
}
