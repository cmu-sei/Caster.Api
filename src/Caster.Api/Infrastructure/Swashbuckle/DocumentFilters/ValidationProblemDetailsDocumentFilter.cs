/*
Copyright 2021 Carnegie Mellon University. All Rights Reserved. 
 Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
*/

using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Caster.Api.Infrastructure.Swashbuckle.DocumentFilters
{
    public class ValidationProblemDetailsDocumentFilter : IDocumentFilter
    {
        public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
        {
            context.SchemaGenerator.GenerateSchema(typeof(ValidationProblemDetails), context.SchemaRepository);
        }
    }
}