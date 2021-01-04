// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;

namespace Caster.Api.Domain.Models
{
    public class ImportResult
    {
        public IEnumerable<File> LockedFiles { get; set; }
    }
}
