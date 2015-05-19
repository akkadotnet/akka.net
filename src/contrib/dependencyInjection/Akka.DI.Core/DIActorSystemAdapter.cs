//-----------------------------------------------------------------------
// <copyright file="DIActorSystemAdapter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.DI.Core
{
    public class DIActorSystemAdapter
    {
        readonly DIExt producer;
        readonly ActorSystem system;
        public DIActorSystemAdapter(ActorSystem system)
        {
            if (system == null) throw new ArgumentNullException("system");
            this.system = system;
            this.producer = system.GetExtension<DIExt>();
        }

        public Props Props(Type actorType) 
        {
            return producer.Props(actorType);
        }

        public Props Props<TActor>() where TActor : ActorBase
        {
            return Props(typeof(TActor));
        }
    }
}

