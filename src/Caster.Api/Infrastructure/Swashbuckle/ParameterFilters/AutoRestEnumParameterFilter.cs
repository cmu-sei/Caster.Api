// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Caster.Api.Infrastructure.Swashbuckle.ParameterFilters
{
    public class AutoRestEnumParameterFilter : IParameterFilter
    {
        public void Apply(IOpenApiParameter parameter, ParameterFilterContext context)
        {
            var type = context.ApiParameterDescription.Type;

            if (type != null && type.IsEnum)
            {
                var extensionData = new JsonObject
                {
                    ["name"] = JsonValue.Create(type.Name),
                    ["modelAsString"] = JsonValue.Create(true)
                };

                parameter.Extensions.Add("x-ms-enum", new JsonNodeExtension(extensionData));
            };
        }
    }
}
