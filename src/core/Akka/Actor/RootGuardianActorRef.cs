//-----------------------------------------------------------------------
// <copyright file="RootGuardianActorRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor.Internal;
using Akka.Dispatch;

namespace Akka.Actor
{
    public class RootGuardianActorRef : LocalActorRef
    {
        private IInternalActorRef _tempContainer;
        private readonly IInternalActorRef _deadLetters;
        private readonly IReadOnlyDictionary<string, IInternalActorRef> _extraNames;

        public RootGuardianActorRef(ActorSystemImpl system, Props props, MessageDispatcher dispatcher, Func<Mailbox> createMailbox, //TODO: switch from  Func<Mailbox> createMailbox to MailboxType mailboxType
            IInternalActorRef supervisor, ActorPath path, IInternalActorRef deadLetters, IReadOnlyDictionary<string, IInternalActorRef> extraNames)
            : base(system,props,dispatcher,createMailbox,supervisor,path)
        {
            _deadLetters = deadLetters;
            _extraNames = extraNames;
        }

        public override IInternalActorRef Parent { get { return this; } }

        public void SetTempContainer(IInternalActorRef tempContainer)
        {
            _tempContainer = tempContainer;
        }

        public override IInternalActorRef GetSingleChild(string name)
        {
            switch(name)
            {
                case "temp":
                    return _tempContainer;
                case "deadLetters":
                    return _deadLetters;
                default:
                    IInternalActorRef extraActorRef;
                    if(_extraNames.TryGetValue(name, out extraActorRef))
                        return extraActorRef;
                    return base.GetSingleChild(name);
            }
        }
    }
}

