// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;

namespace Caster.Api.Features.Workspaces
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Domain.Models.Workspace, Workspace>();
            CreateMap<Create.Command, Domain.Models.Workspace>();
            CreateMap<Edit.Command, Domain.Models.Workspace>();
            CreateMap<PartialEdit.Command, Domain.Models.Workspace>()
                .ForAllMembers(opts => opts.Condition((src, dest, srcMember) => srcMember != null));
        }
    }
}
