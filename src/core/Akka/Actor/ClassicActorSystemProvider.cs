using System;
using System.Collections.Generic;
using System.Text;
using Akka.Annotations;

namespace Akka.Actor
{
    /// <summary>
    /// Glue API introduced to allow minimal user effort integration between classic and typed for example for streams.
    ///
    /// Not for user extension.
    /// </summary>
    [InternalApi]
    public interface IClassicActorSystemProvider
    {
        /// <summary>
        /// Allows access to the classic `akka.actor.ActorSystem` even for `akka.actor.typed.ActorSystem[_]`s.
        /// </summary>
        ActorSystem ClassicSystem { get; }
    }

    /// <summary>
    /// Glue API introduced to allow minimal user effort integration between classic and typed for example for streams.
    ///
    /// Not for user extension.
    /// </summary>
    [InternalApi]
    public interface IClassicActorContextProvider
    {
        /// <summary>
        /// INTERNAL API
        /// </summary>
        IActorContext ClassicActorContext { get; }
    }
}
