// //-----------------------------------------------------------------------
// // <copyright file="IClassicActorSystemProvider.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using Akka.Annotations;

namespace Akka.Actor
{
    /// <summary>
    /// Glue API introduced to allow minimal user effort integration between classic and typed for example for streams.
    /// </summary>
    [DoNotInherit]
    public interface IClassicActorSystemProvider
    {

        [InternalApi]
        ActorSystem ClassicSystem { get; }
    }

    /// <summary>
    /// Glue API introduced to allow minimal user effort integration between classic and typed for example for streams.
    /// </summary>
    [DoNotInherit]
    public interface IClassicActorContextProvider
    {
        [InternalApi]
        IActorContext ClassicActorContext { get; }
    }
    
}