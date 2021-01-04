// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Caster.Api.Infrastructure.Authorization
{
    public enum CasterClaimTypes
    {
        SystemAdmin,
        ContentDeveloper,
        Operator,
        BaseUser
    }
}

