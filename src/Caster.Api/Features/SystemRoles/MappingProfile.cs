// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Caster.Api.Features.SystemRoles
{
    using AutoMapper;
    using System.Linq;

    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Domain.Models.SystemRole, SystemRole>();
            CreateMap<Create.Command, Domain.Models.SystemRole>();
            CreateMap<Edit.Command, Domain.Models.SystemRole>();
        }
    }
}
