// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Caster.Api.Infrastructure.Swashbuckle.SchemaFilters
{
    public class AutoRestEnumSchemaFilter : ISchemaFilter
    {
        public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
        {
            if (schema?.Extensions == null) return;

            var type = context.Type;
            if (type.IsEnum)
            {
                var extensionData = new JsonObject
                {
                    ["name"] = JsonValue.Create(type.Name),
                    ["modelAsString"] = JsonValue.Create(true)
                };

                schema.Extensions.Add("x-ms-enum", new JsonNodeExtension(extensionData));
            };
        }
    }
}
