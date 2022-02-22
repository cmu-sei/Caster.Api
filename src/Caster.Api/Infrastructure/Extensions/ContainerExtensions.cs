// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Caster.Api.Features.Files;
using MediatR;
using MediatR.Pipeline;
using SimpleInjector;

namespace Caster.Api.Infrastructure.Extensions
{
    public static class ContainerExtensions
    {
        public static Container AddMediator(this Container container, params Assembly[] assemblies)
        {
            return BuildMediator(container, (IEnumerable<Assembly>)assemblies);
        }

        public static Container BuildMediator(this Container container, IEnumerable<Assembly> assemblies)
        {
            var allAssemblies = new List<Assembly> { typeof(IMediator).GetTypeInfo().Assembly };
            allAssemblies.AddRange(assemblies);

            container.Register<IMediator, Mediator>();
            container.Register(typeof(IRequestHandler<,>), allAssemblies);

            container.Collection.Register(typeof(INotificationHandler<>), GetTypesToRegister(typeof(INotificationHandler<>), container, assemblies));
            container.Collection.Register(typeof(IPipelineBehavior<,>), GetTypesToRegister(typeof(IPipelineBehavior<,>), container, assemblies));
            container.Collection.Register(typeof(IRequestPreProcessor<>), GetTypesToRegister(typeof(IRequestPreProcessor<>), container, assemblies));
            container.Collection.Register(typeof(IRequestPostProcessor<,>), GetTypesToRegister(typeof(IRequestPostProcessor<,>), container, assemblies));

            container.Register(() => new ServiceFactory(container.GetInstance));
            return container;
        }

        private static IEnumerable<Type> GetTypesToRegister(Type type, Container container, IEnumerable<Assembly> assemblies)
        {
            // we have to do this because by default, generic type definitions (such as the Constrained Notification Handler) won't be registered
            return container.GetTypesToRegister(type, assemblies, new TypesToRegisterOptions
            {
                IncludeGenericTypeDefinitions = true,
                IncludeComposites = false,
            });
        }
    }
}
