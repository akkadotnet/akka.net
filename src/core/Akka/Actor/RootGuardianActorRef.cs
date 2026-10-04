//-----------------------------------------------------------------------
// <copyright file="RootGuardianActorRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// Creates the root guardian reference and its actor cell.
        /// </summary>
        /// <param name="system">The actor system that owns the guardian.</param>
        /// <param name="props">The guardian actor configuration.</param>
        /// <param name="dispatcher">The dispatcher used by the guardian.</param>
        /// <param name="mailboxType">The guardian's mailbox type.</param>
        /// <param name="supervisor">The guardian's supervisor.</param>
        /// <param name="path">The root guardian's actor path.</param>
        /// <param name="deadLetters">The system's dead-letter reference.</param>
        /// <param name="extraNames">Additional named references resolved directly beneath the guardian.</param>
        public RootGuardianActorRef(ActorSystemImpl system, Props props, MessageDispatcher dispatcher, MailboxType mailboxType, 
            IInternalActorRef supervisor, ActorPath path, IInternalActorRef deadLetters, IReadOnlyDictionary<string, IInternalActorRef> extraNames)
            : base(system,props,dispatcher,mailboxType,supervisor,path)
        {
            _deadLetters = deadLetters;
            _extraNames = extraNames;
        }


        /// <summary>
        /// The root guardian supervises itself.
        /// </summary>
        public override IInternalActorRef Parent { get { return this; } }

        /// <summary>
        /// Sets the temporary child container used while guardian children are being initialized.
        /// </summary>
        /// <param name="tempContainer">The temporary container exposed at the <c>temp</c> path.</param>
        public void SetTempContainer(IInternalActorRef tempContainer)
        {
            _tempContainer = tempContainer;
        }

        /// <summary>
        /// Resolves a direct child, including the guardian's built-in names.
        /// </summary>
        /// <param name="name">The direct child name to resolve.</param>
        /// <returns>The temporary container, dead letters, a registered extra reference, or a regular child reference.</returns>
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
