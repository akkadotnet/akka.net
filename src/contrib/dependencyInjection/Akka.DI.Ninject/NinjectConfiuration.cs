using Akka.Actor;
using Akka.DI.Core;
using Ninject;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Akka.DI.Ninject
{
    public class NinjectConfiuration : IContainerConfiguration
    {
       IKernel container;

        private ConcurrentDictionary<string, Type> typeCache;

        public NinjectConfiuration(IKernel container)
        {
            if (container == null) throw new ArgumentNullException("container");
            this.container = container;
            typeCache = new ConcurrentDictionary<string, Type>(StringComparer.InvariantCultureIgnoreCase);
        }

        public Type GetType(string ActorName)
        {
            

            if (!typeCache.ContainsKey(ActorName))
                typeCache.TryAdd(ActorName, ActorName.GetTypeValue());

            return typeCache[ActorName];
        }

        public Func<ActorBase> CreateActorFactory(string ActorName)
        {
            return () =>
            {
                Type actorType = this.GetType(ActorName);

                return (ActorBase)container.GetService(actorType);
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
