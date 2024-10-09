// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using AutoMapper;
using AutoMapper.Internal;

namespace Caster.Api.Features.Resources
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Domain.Models.Resource, Resource>();

            // Allows passing in of a property name to exclude it from mapping at runtime
            this.Internal().ForAllMaps((typeMap, mappingExpression) =>
            {
                mappingExpression.ForAllMembers(memberOptions =>
                {
                    memberOptions.Condition((o1, o2, o3, o4, resolutionContext) =>
                    {
                        var name = memberOptions.DestinationMember.Name;
                        if (resolutionContext.Items.TryGetValue(MemberExclusionKey, out object exclusions))
                        {
                            if (((IEnumerable<string>)exclusions).Contains(name))
                            {
                                return false;
                            }
                        }
                        return true;
                    });
                });
            });
        }

        public static string MemberExclusionKey { get; } = "exclude";
    }

    public static class IMappingOperationOptionsExtensions
    {
        public static void ExcludeMembers(this AutoMapper.IMappingOperationOptions options, params string[] members)
        {
            options.Items[MappingProfile.MemberExclusionKey] = members;
        }
    }
}
