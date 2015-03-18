﻿using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DI.Core
{
    public class DIActorAdapter
    {
        readonly DIExt producer;
        readonly IActorContext context;
        public DIActorAdapter(IActorContext context)
        {
            if (context == null) throw new ArgumentNullException("context");
            this.context = context;
            this.producer = context.System.GetExtension<DIExt>();
        }
        public ActorRef ActorOf<TActor>(string name = null) where TActor : ActorBase, new()
        {
            return context.ActorOf(producer.Props(typeof(TActor).Name), name);
        }
    }
}
