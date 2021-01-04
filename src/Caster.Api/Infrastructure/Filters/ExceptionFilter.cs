// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Caster.Api.Infrastructure.Exceptions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Net;
using Microsoft.Extensions.Hosting;

namespace Caster.Api.Infrastructure.Filters
{
    public class ExceptionFilter : IExceptionFilter
    {
        private readonly IWebHostEnvironment _env;

        public ExceptionFilter(IWebHostEnvironment env)
        {
            _env = env;
        }

        public void OnException(ExceptionContext context)
        {
            var error = new ProblemDetails();
            error.Status = GetStatusCodeFromException(context.Exception);

            if(error.Status == (int)HttpStatusCode.InternalServerError)
            {
                if (_env.IsDevelopment())
                {
                    error.Title = context.Exception.Message;
                    error.Detail = context.Exception.ToString();
                }
                else
                {
                    error.Title = "A server error occurred.";
                    error.Detail = context.Exception.Message;
                }
            }
            else
            {
                error.Title = context.Exception.Message;
            }

            context.Result = new JsonResult(error)
            {
                StatusCode = error.Status
            };
        }

        /// <summary>
        /// map all custom exceptions to proper http status code
        /// </summary>
        /// <returns></returns>
        private int GetStatusCodeFromException(Exception exception)
        {
            HttpStatusCode statusCode = HttpStatusCode.InternalServerError;

            if (exception is IApiException)
            {
                statusCode = (exception as IApiException).GetStatusCode();
            }

            return (int)statusCode;
        }
    }
}
