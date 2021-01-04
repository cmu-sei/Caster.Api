// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Caster.Api.Features.Projects
{
    using System.Linq;
    using AutoMapper;
    using Caster.Api.Domain.Models;

    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Domain.Models.Project, Project>();
            CreateMap<Create.Command, Domain.Models.Project>();
            CreateMap<Edit.Command, Domain.Models.Project>();
            CreateMap<ImportResult, Import.ImportProjectResult>()
                .ForMember(dest => dest.LockedFiles, opt => opt.MapFrom((src) => src.LockedFiles.Select(x => x.Path)));
        }
    }
}
