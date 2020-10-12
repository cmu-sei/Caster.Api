/*
Crucible
Copyright 2020 Carnegie Mellon University.
NO WARRANTY. THIS CARNEGIE MELLON UNIVERSITY AND SOFTWARE ENGINEERING INSTITUTE MATERIAL IS FURNISHED ON AN "AS-IS" BASIS. CARNEGIE MELLON UNIVERSITY MAKES NO WARRANTIES OF ANY KIND, EITHER EXPRESSED OR IMPLIED, AS TO ANY MATTER INCLUDING, BUT NOT LIMITED TO, WARRANTY OF FITNESS FOR PURPOSE OR MERCHANTABILITY, EXCLUSIVITY, OR RESULTS OBTAINED FROM USE OF THE MATERIAL. CARNEGIE MELLON UNIVERSITY DOES NOT MAKE ANY WARRANTY OF ANY KIND WITH RESPECT TO FREEDOM FROM PATENT, TRADEMARK, OR COPYRIGHT INFRINGEMENT.
Released under a MIT (SEI)-style license, please see license.txt or contact permission@sei.cmu.edu for full terms.
[DISTRIBUTION STATEMENT A] This material has been approved for public release and unlimited distribution.  Please see Copyright notice for non-US Government use and distribution.
Carnegie Mellon(R) and CERT(R) are registered in the U.S. Patent and Trademark Office by Carnegie Mellon University.
DM20-0181
*/

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
                .ForMember(m => m.Workspaces, opt => opt.ExplicitExpansion());
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
