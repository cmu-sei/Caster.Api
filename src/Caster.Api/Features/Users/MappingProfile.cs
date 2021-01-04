// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Caster.Api.Features.Users
{
    using AutoMapper;
    using System.Linq;

    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Domain.Models.User, User>()
                .ForMember(m => m.Permissions, opt => opt.MapFrom(x => x.UserPermissions.Select(y => y.Permission)))
                .ForMember(m => m.Permissions, opt => opt.ExplicitExpansion());
            CreateMap<Create.Command, Domain.Models.User>();
            CreateMap<Edit.Command, Domain.Models.User>();
        }
    }
}
