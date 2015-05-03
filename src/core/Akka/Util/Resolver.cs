//-----------------------------------------------------------------------
// <copyright file="Resolver.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using System;

namespace Akka.Util
{
    public interface IResolver
    {
        T Resolve<T>(object[] args);
    }

    public abstract class Resolve : IIndirectActorProducer
    {
        public abstract ActorBase Produce();
        public abstract Type ActorType { get; }

        protected static IResolver Resolver { get; private set; }
        public static void SetResolver(IResolver resolver)
        {
            Resolver = resolver;
        }


        public void Release(ActorBase actor)
        {
            actor = null;
        }
    }

    public class Resolve<TActor> : Resolve where TActor : ActorBase
    {
        public Resolve(params object[] args)
        {
            Arguments = args;
        }

        public override ActorBase Produce()
        {
            if (Resolver == null) {
                throw new InvalidOperationException("Resolver is not initialized");
            }
            return Resolver.Resolve<TActor>(Arguments);
        }

        public override Type ActorType { get { return typeof(TActor); } }
        public object[] Arguments { get; private set; }
    }
}

