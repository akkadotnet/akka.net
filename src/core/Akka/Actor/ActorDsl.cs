using System;
using System.Linq.Expressions;
using Akka.Configuration;
using Akka.Routing;

namespace Akka.Actor
{
    public static class ActorDsl
    {
        public static ActorSystem ActorSystem(string name, Config config)
        {
            return Actor.ActorSystem.Create(name, config);
        }

        public static ActorSystem ActorSystem(string name)
        {
            return Actor.ActorSystem.Create(name);
        }

        public static Props Props<T>() where T : ActorBase, new()
        {
            return Actor.Props.Create<T>();
        }

        public static Props Props<T>(Expression<Func<T>> factory) where T : ActorBase
        {
            return Actor.Props.Create(factory);
        }

        public static FromConfig FromConfig()
        {
            return Routing.FromConfig.Instance;
        }

        public static Identify Identify(object messageId) => new Identify(messageId);
        public static PoisonPill PoisonPill() => Actor.PoisonPill.Instance;

        public static Status.Success Success(object status)
        {
            return new Status.Success(status);
        }

        public static Status.Failure Failure(Exception cause)
        {
            return new Status.Failure(cause);
        }
    }
}