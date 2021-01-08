//-----------------------------------------------------------------------
// <copyright file="RootGuardianActorRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor.Internal;
using Akka.Annotations;
using Akka.Dispatch;

namespace Akka.Actor
{
    /// <summary>
    /// INTERNAL API.
    /// 
    /// Used by <see cref="GuardianActor"/>
    /// </summary>
    [InternalApi]
    public class RootGuardianActorRef : LocalActorRef
    {
        private IInternalActorRef _tempContainer;
        private readonly IInternalActorRef _deadLetters;
        private readonly IReadOnlyDictionary<string, IInternalActorRef> _extraNames;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <param name="props">TBD</param>
        /// <param name="dispatcher">TBD</param>
        /// <param name="mailboxType">TBD</param>
        /// <param name="supervisor">TBD</param>
        /// <param name="path">TBD</param>
        /// <param name="deadLetters">TBD</param>
        /// <param name="extraNames">TBD</param>
        public RootGuardianActorRef(ActorSystemImpl system, Props props, MessageDispatcher dispatcher, MailboxType mailboxType, 
            IInternalActorRef supervisor, ActorPath path, IInternalActorRef deadLetters, IReadOnlyDictionary<string, IInternalActorRef> extraNames)
            : base(system,props,dispatcher,mailboxType,supervisor,path)
        {
            _deadLetters = deadLetters;
            _extraNames = extraNames;
        }


        /// <summary>
        /// TBD
        /// </summary>
        public override IInternalActorRef Parent { get { return this; } }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="tempContainer">TBD</param>
        public void SetTempContainer(IInternalActorRef tempContainer)
        {
            _tempContainer = tempContainer;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public override IInternalActorRef GetSingleChild(string name)
        {
            switch(name)
            {
                case "temp":
                    return _tempContainer;
                case "deadLetters":
                    return _deadLetters;
                default:
                    if(_extraNames.TryGetValue(name, out var extraActorRef))
                        return extraActorRef;
                    return base.GetSingleChild(name);
            }
        }
    }
}

