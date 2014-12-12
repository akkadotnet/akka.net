using Akka.Actor;
using Akka.DI.Core;
using Autofac;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Akka.DI.AutoFac
{
    public class AutoFacConfiguration : IContainerConfiguration
    {
        private IContainer container;
        private ConcurrentDictionary<string, Type> typeCache;

        public AutoFacConfiguration(IContainer container)
        {
            this.container = container;
            typeCache = new ConcurrentDictionary<string, Type>(StringComparer.InvariantCultureIgnoreCase);
        }

        public Type GetType(string ActorName)
        {
            if (!typeCache.ContainsKey(ActorName))
                typeCache.TryAdd(ActorName,
                ActorName.GetTypeValue() ??
                container.
                ComponentRegistry.
                Registrations.
                Where(registration => registration.Activator.LimitType.
                    Name.Equals(ActorName, StringComparison.InvariantCultureIgnoreCase)).
                    Select(regisration => regisration.Activator.LimitType).
                    FirstOrDefault());

            return typeCache[ActorName];

        }
       
        public Func<ActorBase> CreateActorFactory(string ActorName)
        {
            return () =>
            {
                Type actorType = this.GetType(ActorName);
                return (ActorBase)container.Resolve(actorType);
            };
        }
        
    }
    internal static class Extensions
    {
        public static Type GetTypeValue(this string typeName)
        {
            var firstTry = Type.GetType(typeName);
            Func<Type> searchForType = () =>
            {
                return
                AppDomain.
                    CurrentDomain.
                    GetAssemblies().
                    SelectMany(x => x.GetTypes()).
                    Where(t => t.Name.Equals(typeName)).
                    FirstOrDefault();
            };
            return firstTry ?? searchForType();
        }

    }
}
