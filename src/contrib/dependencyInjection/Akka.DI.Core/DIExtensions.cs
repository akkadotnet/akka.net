using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Castle.Windsor;
using Autofac.Builder;
using Autofac.Core;
using Autofac;
using System.Collections.Concurrent;

namespace Fauux.Banque.Harness
{
    public static class ActorSystemExtensions
    {
        public static void ActorOf<TActor>(this ActorSystem system, string Name) where TActor : ActorBase
        {
            system.ActorOf(DIExtension.DIExtensionProvider.Get(system).Props(typeof(TActor).Name), Name);
        }
    }
    public static class StringExtensions
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
    public class NinjectConfiuration : IContainerConfiguration
    {
        Ninject.IKernel container;
        private ConcurrentDictionary<string, Type> typeCache;

        public NinjectConfiuration(Ninject.IKernel container)
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
                typeCache.TryAdd(ActorName, ActorName.GetTypeValue());

            return typeCache[ActorName];
        }

        public Func<ActorBase> CreateActor(string ActorName)
        {
            return () =>
            {
                Type actorType = this.GetType(ActorName);
                
                return (ActorBase)container.GetService(actorType);
            };
            
        }
    }
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
        public ActorSystem CreateActorSystem(string SystemName)
        {
            var system = ActorSystem.Create(SystemName);
            system.RegisterExtension((IExtensionId)DIExtension.DIExtensionProvider);

            DIExtension.DIExtensionProvider.Get(system).Initialize(this);
            return system;
        }           
        public Func<ActorBase> CreateActor(string ActorName)
        {
            return () =>
            {
                Type actorType = this.GetType(ActorName);
                return (ActorBase)container.Resolve(actorType);
            };
        }
    }
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


    public interface IContainerConfiguration
    {
        Type GetType(string ActorName);
        Func<ActorBase> CreateActor(string ActorName);
        ActorSystem CreateActorSystem(string SystemName);
    }

    public class DIExt : IExtension
    {
        private IContainerConfiguration applicationContext;

        public void Initialize(IContainerConfiguration applicationContext)
        {
            this.applicationContext = applicationContext;
        }
        public Props Props(String actorName)
        {
            return new Props(typeof(DIActorProducerClass),  new object[] { applicationContext, actorName });
        }

    }
    public class DIExtension : ExtensionIdProvider<DIExt> 
    {
        public static DIExtension DIExtensionProvider = new DIExtension();

     
        public override DIExt CreateExtension(ExtendedActorSystem system)
        {
            var extension = new DIExt();
            return extension;
        }
    }

    public class DIActorProducerClass : IndirectActorProducer
    {
        private IContainerConfiguration applicationContext;
        private string actorName;
        readonly Func<ActorBase> myActor;

        public DIActorProducerClass(IContainerConfiguration applicationContext,
                                    string actorName)
        {
            this.applicationContext = applicationContext;
            this.actorName = actorName;
            this.myActor = applicationContext.CreateActor(actorName);
        }
        public Type ActorType
        {
            get { return this.applicationContext.GetType(this.actorName); }
        }

        public ActorBase Produce()
        {
            return myActor();
        }
        
    }
}
