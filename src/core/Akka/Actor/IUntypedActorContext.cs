using System;

namespace Akka.Actor
{
    /// <summary>
    /// Interface IUntypedActorContext
    /// </summary>
    public interface IUntypedActorContext : IActorContext
    {
        [Obsolete("Use Become or BecomeStacked instead. This method will be removed in future versions")]
        void Become(UntypedReceive receive, bool discardOld = true);

        /// <summary>
        /// Changes the actor's behavior and replaces the current receive handler with the specified handler.
        /// </summary>
        /// <param name="receive">The new message handler.</param>
        void Become(UntypedReceive receive);

        /// <summary>
        /// Changes the actor's behavior and replaces the current receive handler with the specified handler.
        /// The current handler is stored on a stack, and you can revert to it by calling <see cref="IUntypedActorContext.UnbecomeStacked"/>
        /// <remarks>Please note, that in order to not leak memory, make sure every call to <see cref="BecomeStacked"/>
        /// is matched with a call to <see cref="IUntypedActorContext.UnbecomeStacked"/>.</remarks>
        /// </summary>
        /// <param name="receive">The new message handler.</param>
        void BecomeStacked(UntypedReceive receive);
    }
}