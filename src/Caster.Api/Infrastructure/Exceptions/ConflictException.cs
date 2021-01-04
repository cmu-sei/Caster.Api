// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;

namespace Caster.Api.Infrastructure.Exceptions
{
    public class ConflictException : Exception, IApiException
    {
        public ConflictException()
            : base("Cannot perform action due to a conflict.")
        {
        }

        public ConflictException(string message)
            : base(message)
        {
        }

        public HttpStatusCode GetStatusCode()
        {
            return HttpStatusCode.Conflict;
        }
    }
}
