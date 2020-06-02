using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Akka.Actor;

namespace Akka.Remote.Artery
{
    // ARTERY: Incomplete implementation
    internal class Association
    {
        /// <summary>
        /// Holds reference to shared state of Association - *access only via helper methods*
        /// </summary>
        private volatile AssociationState _sharedStateDoNotCallMeDirectly = new AssociationState();

        public bool SwapState(AssociationState oldState, AssociationState newState)
        {
            // ARTERY: stub function
            return false;
        }

        public AssociationState AssociationState => Volatile.Read(ref _sharedStateDoNotCallMeDirectly);

        public int SendTerminationHint(IActorRef replyTo)
        {
            // ARTERY: stub function
            return 0;
        }
    }
}
