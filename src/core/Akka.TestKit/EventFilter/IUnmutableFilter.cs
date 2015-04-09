//-----------------------------------------------------------------------
// <copyright file="IUnmutableFilter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.TestKit
{
    // ReSharper disable once InconsistentNaming
    public interface IUnmutableFilter : IDisposable
    {
        /// <summary>
        /// Call this to let events that previously have been muted to be logged again.
        /// </summary>
        void Unmute();
    }
}

