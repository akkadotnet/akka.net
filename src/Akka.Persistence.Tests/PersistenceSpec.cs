using System;
using System.Linq.Expressions;
using Akka.Actor;
using Akka.TestKit;

namespace Akka.Persistence.Tests
{
    public abstract class PersistenceSpec : AkkaSpec
    {
        protected sealed class GetState
        {
            public static readonly GetState Instance = new GetState();

            private GetState()
            {
            }
        }

        public string Name { get; set; }

        protected ActorRef NamedProcessor<T>(Expression<Func<T>> expr) where T : PersistentActorBase
        {
            return Sys.ActorOf(Props.Create(expr), Name);
        }
    }
}