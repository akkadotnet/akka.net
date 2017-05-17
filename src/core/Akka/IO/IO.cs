//-----------------------------------------------------------------------
// <copyright file="IO.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#if AKKAIO
using Akka.Actor;

namespace Akka.IO
{
    /// <summary>
    /// TBD
    /// </summary>
    public abstract class IOExtension : IExtension
    {
        /// <summary>
        /// TBD
        /// </summary>
        public abstract IActorRef Manager { get; }
    }
}
#endif