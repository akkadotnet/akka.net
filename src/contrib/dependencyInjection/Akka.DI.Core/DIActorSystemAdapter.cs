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
        public IActorRef ActorOf<TActor>(string name = null) where TActor : ActorBase
        {
            return system.ActorOf(producer.Props(typeof(TActor)), name);
        }

        public Props Props<TActor>() where TActor : ActorBase
        {
            return producer.Props(typeof(TActor));
        }
    }
}

