using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.DI.Core;
using SimpleInjector;
using SimpleInjector.Extensions.ExecutionContextScoping;
using SI = SimpleInjector;

namespace Akka.DI.SimpleInjector
{
    public class SimpleInjectorDependencyResolver : IDependencyResolver
    {
        private readonly Container _container;
        private readonly ActorSystem _actorSystem;
        private readonly ConcurrentDictionary<string, Type> _typeCache = new ConcurrentDictionary<string, Type>(StringComparer.InvariantCultureIgnoreCase);
        private readonly ConditionalWeakTable<ActorBase, SI.Scope> _references = new ConditionalWeakTable<ActorBase, SI.Scope>();

        public SimpleInjectorDependencyResolver(Container container, ActorSystem actorSystem)
        {
            if (container == null) throw new ArgumentNullException("container");
            if (actorSystem == null) throw new ArgumentNullException("actorSystem");
            _container = container;
            _actorSystem = actorSystem;
            _actorSystem.AddDependencyResolver(this);
        }

        public Type GetType(string actorName)
        {
            return _typeCache.GetOrAdd(actorName, actorName.GetTypeValue());
        }

        public Func<ActorBase> CreateActorFactory(Type actorType)
        {
            return () =>
            {
                var container = _container;
                var references = _references;
                var scope = container.BeginExecutionContextScope();
                var actor = (ActorBase)container.GetInstance(actorType);
                references.Add(actor, scope);

                return actor;
            };
        }

        public Props Create<TActor>() where TActor : ActorBase
        {
            return Create(typeof(TActor));
        }

        public Props Create(Type actorType)
        {
            return _actorSystem.GetExtension<DIExt>().Props(actorType);
        }

        public void Release(ActorBase actor)
        {
            SI.Scope scope;

            if (!_references.TryGetValue(actor, out scope)) return;

            scope.Dispose();
            _references.Remove(actor);
        }
    }
}
