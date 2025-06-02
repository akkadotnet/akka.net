//-----------------------------------------------------------------------
// <copyright file="InternalCurrentActorCellKeeper.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#nullable enable
using System;

namespace Akka.Actor.Internal
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    public static class InternalCurrentActorCellKeeper
    {
        [ThreadStatic]
        private static ActorCell? _current;


        /// <summary>
        /// TBD
        /// 
        /// INTERNAL!
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        // ReSharper disable once ConvertToAutoProperty
        public static ActorCell? Current { get { return _current; } set { _current = value; } }
    }
}

