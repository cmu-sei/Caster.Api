// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Caster.Api.Infrastructure.Serialization
{
    public class NumberToStringConverter : JsonConverter<object>
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return typeof(string) == typeToConvert;
        }
        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if(reader.TokenType == JsonTokenType.Number) {
                return reader.TryGetInt64(out long l) ?
                    l.ToString():
                    reader.GetDouble().ToString();
            }
            if(reader.TokenType == JsonTokenType.String) {
                return reader.GetString();
            }
            using(JsonDocument document = JsonDocument.ParseValue(ref reader)){
                return document.RootElement.Clone().ToString();
            }
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            writer.WriteStringValue( value.ToString());
        }
    }
}
