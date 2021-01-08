//-----------------------------------------------------------------------
// <copyright file="InternalCurrentActorCellKeeper.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor.Internal
{
    /// <summary>
    /// TBD
    /// 
    /// INTERNAL!
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public static class InternalCurrentActorCellKeeper
    {
        [ThreadStatic]
        private static ActorCell _current;


        /// <summary>
        /// TBD
        /// 
        /// INTERNAL!
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public static ActorCell Current { get { return _current; } set { _current = value; } }
    }
}

