// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using MediatR;

namespace Caster.Api.Features.Plans
{
    public class RunAdded : INotification 
    {
        public Guid RunId { get; set; }
    }
}
