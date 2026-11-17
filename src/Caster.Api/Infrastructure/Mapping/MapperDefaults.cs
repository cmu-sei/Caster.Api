// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Riok.Mapperly.Abstractions;

// Defaults for every Mapperly [Mapper] in the application. .editorconfig raises unmapped members and
// projections that cannot be inlined to errors, so a member that stops being mapped fails the build.
[assembly: MapperDefaults(
    // Outbound maps (entity to view model) must fill every member of the view model, so a response
    // never silently carries a default. Inbound maps (command onto entity) ask the opposite question
    // instead, with [MapperRequiredMapping(RequiredMappingStrategy.Source)]: every command member must
    // reach the entity, while Id, navigation properties and server-assigned fields are left alone.
    RequiredMappingStrategy = RequiredMappingStrategy.Target,

    // A null source value is skipped rather than written when the target cannot hold null, so the
    // target keeps the value it already had: a null Guid? id on a create leaves the new entity's id
    // alone, and a null bool? or int? in a PATCH keeps the stored value. False is Mapperly's default,
    // stated here so it cannot change unnoticed.
    //
    // This is broader than the AutoMapper convention it replaced, which only skipped a nullable value
    // type onto its own underlying type (Guid? to Guid). Mapperly also skips a converting pair such as
    // int? to long, where AutoMapper wrote 0, and, in a project with nullable reference types enabled,
    // string? to string, where AutoMapper wrote null. Caster.Api has nullable reference types disabled
    // and no converting pairs in its commands, so the two agree here.
    ThrowOnPropertyMappingNullMismatch = false)]
