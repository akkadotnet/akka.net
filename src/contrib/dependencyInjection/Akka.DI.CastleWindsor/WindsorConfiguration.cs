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
    public class WindsorConfiguration : IContainerConfiguration
    {
        private IWindsorContainer container;
        private ConcurrentDictionary<string, Type> typeCache;

        public WindsorConfiguration(IWindsorContainer container)
        {
            if (container == null) throw new ArgumentNullException("container");
            this.container = container;
            typeCache = new ConcurrentDictionary<string, Type>(StringComparer.InvariantCultureIgnoreCase);
        }

        public ActorSystem CreateActorSystem(string SystemName)
        {
            if (SystemName == null) throw new ArgumentNullException("SystemName");
            var system = ActorSystem.Create(SystemName);
            system.RegisterExtension((IExtensionId)DIExtension.DIExtensionProvider);

            DIExtension.DIExtensionProvider.Get(system).Initialize(this);
            return system;
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

        public Func<ActorBase> CreateActor(string ActorName)
        {
            return () => (ActorBase)container.Resolve(GetType(ActorName));
        }



    }
}
