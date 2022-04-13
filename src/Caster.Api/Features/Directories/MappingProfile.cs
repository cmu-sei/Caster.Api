// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using AutoMapper;
using Caster.Api.Domain.Models;

namespace Caster.Api.Features.Directories
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Domain.Models.Directory, Directory>()
                .ForMember(m => m.Files, opt => opt.ExplicitExpansion())
                .ForMember(m => m.Workspaces, opt => opt.ExplicitExpansion())
                .ForMember(m => m.Designs, opt => opt.ExplicitExpansion());
            CreateMap<Create.Command, Domain.Models.Directory>();
            CreateMap<Edit.Command, Domain.Models.Directory>();
            CreateMap<PartialEdit.Command, Domain.Models.Directory>()
                .ForMember(dest => dest.ParentId, opt => opt.MapFrom((src, dest) => src.ParentId.HasValue ? src.ParentId.Value : dest.ParentId))
                .ForAllOtherMembers(opts => opts.Condition((src, dest, srcMember) => srcMember != null));
            CreateMap<ImportResult, Import.ImportDirectoryResult>()
                .ForMember(dest => dest.LockedFiles, opt => opt.MapFrom((src) => src.LockedFiles.Select(x => x.Path)));
        }
    }
}
