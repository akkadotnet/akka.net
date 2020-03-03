//-----------------------------------------------------------------------
// <copyright file="WatchedActorTerminatedException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Streams.Dsl;

namespace Akka.Streams
{
    /// <summary>
    /// Used as failure exception by an `ask` operator if the target actor terminates.
    /// </summary>
    /// <seealso cref="Flow{TIn,TOut,TMat}.Ask{TOut2}"/>
    /// <seealso cref="Source{TOut,TMat}.Ask{TOut2}"/>
    /// <seealso cref="FlowOperations.Watch{T,TMat}"/>
    /// <seealso cref="SourceOperations.Watch{T,TMat}"/>
    public class WatchedActorTerminatedException : AkkaException
    {
        public WatchedActorTerminatedException(string stageName, IActorRef actorRef) 
            : base($"Actor watched by [{stageName}] has terminated! Was: {actorRef}")
        { }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="AkkaException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected WatchedActorTerminatedException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }
}
