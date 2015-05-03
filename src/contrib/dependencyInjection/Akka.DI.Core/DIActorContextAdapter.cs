//-----------------------------------------------------------------------
// <copyright file="DIActorContextAdapter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        public IActorRef ActorOf<TActor>(string name = null) where TActor : ActorBase
        {
            return context.ActorOf(producer.Props(typeof(TActor)), name);
        }
    }
}

