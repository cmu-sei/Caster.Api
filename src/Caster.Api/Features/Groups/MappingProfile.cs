// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Caster.Api.Features.Groups
{
    using System.Linq;
    using AutoMapper;
    using Caster.Api.Domain.Models;

    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Domain.Models.Group, Group>();
            CreateMap<Create.Command, Domain.Models.Group>();
            CreateMap<Edit.Command, Domain.Models.Group>();

            CreateMap<Domain.Models.GroupMembership, GroupMembership>();
        }
    }
}
