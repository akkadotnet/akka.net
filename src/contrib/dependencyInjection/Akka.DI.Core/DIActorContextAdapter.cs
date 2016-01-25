//-----------------------------------------------------------------------
// <copyright file="DIActorContextAdapter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.DI.Core
{
    public class DIActorContextAdapter
    {
        readonly DIExt producer;
        readonly IActorContext context;
        public DIActorContextAdapter(IActorContext context)
        {
            if (context == null) throw new ArgumentNullException("context");
            this.context = context;
            this.producer = context.System.GetExtension<DIExt>();
        }

        [Obsolete("Use Props methods for actor creation. This method will be removed in future versions")]
        public IActorRef ActorOf<TActor>(string name = null) where TActor : ActorBase
        {
            return context.ActorOf(producer.Props(typeof(TActor)), name);
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

