// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Caster.Api.Features.UserPermissions
{
    using AutoMapper;    

    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Domain.Models.UserPermission, UserPermission>();
            CreateMap<Create.Command, Domain.Models.UserPermission>();
            CreateMap<Edit.Command, Domain.Models.UserPermission>();
        }
    }
}
