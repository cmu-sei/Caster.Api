// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Text.Json.Serialization;

namespace Caster.Api.Features.Files.Interfaces
{
    public interface IFileUpdateRequest
    {
        bool UpdateContent { get; }
    }

    public abstract class FileUpdateRequest : IFileUpdateRequest
    {
        public abstract string Content { get; set; }

        [JsonIgnore]
        public bool UpdateContent
        {
            get
            {
                return !string.IsNullOrEmpty(this.Content);
            }
        }
    }

    public abstract class FileMetadataUpdateRequest : IFileUpdateRequest
    {
        public bool UpdateContent
        {
            get
            {
                return false;
            }
        }
    }
}

