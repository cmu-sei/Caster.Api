// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.CodeAnalysis;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Caster.Api.Infrastructure.Serialization
{
    public class OptionalGuidConverter : JsonConverter<Optional<Guid?>>
    {
        public override Optional<Guid?> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return new Optional<Guid?>(null);
            }

            if (reader.TokenType == JsonTokenType.None)
            {
                return new Optional<Guid?>();
            }

            Guid innerValue;

            if (reader.TryGetGuid(out innerValue))
            {
                return new Optional<Guid?>(innerValue);
            }
            else
            {
                return new Optional<Guid?>(null);
            }
        }

        public override void Write(Utf8JsonWriter writer, Optional<Guid?> value, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }
    }

    public class OptionalIntConverter : JsonConverter<Optional<int?>>
    {
        public override Optional<int?> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return new Optional<int?>(null);
            }

            if (reader.TokenType == JsonTokenType.None)
            {
                return new Optional<int?>();
            }

            int innerValue;

            if (reader.TryGetInt32(out innerValue))
            {
                return new Optional<int?>(innerValue);
            }
            else
            {
                return new Optional<int?>(null);
            }
        }

        public override void Write(Utf8JsonWriter writer, Optional<int?> value, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }
    }
}
