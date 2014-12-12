using Akka.Actor;
using Akka.DI.Core;
using Castle.Windsor;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Akka.DI.CastleWindsor
{
    public class WindsorDependencyResolver : IDependencyResolver
    {
        private IWindsorContainer container;
        private ConcurrentDictionary<string, Type> typeCache;

        public WindsorDependencyResolver(IWindsorContainer container)
        {
            if (container == null) throw new ArgumentNullException("container");
            this.container = container;
            typeCache = new ConcurrentDictionary<string, Type>(StringComparer.InvariantCultureIgnoreCase);
        }

      
        public Type GetType(string ActorName)
        {
            if (!typeCache.ContainsKey(ActorName))

                typeCache.TryAdd(ActorName,
                                 ActorName.GetTypeValue() ??
                                 container.
                                 Kernel.
                                 GetAssignableHandlers(typeof(object)).
                                 Where(handler => handler.ComponentModel.Name.Equals(ActorName, StringComparison.InvariantCultureIgnoreCase)).
                                 Select(handler => handler.ComponentModel.Implementation).
                                 FirstOrDefault());

            return typeCache[ActorName];
        }

        public Func<ActorBase> CreateActorFactory(string ActorName)
        {
            return () => (ActorBase)container.Resolve(GetType(ActorName));
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
