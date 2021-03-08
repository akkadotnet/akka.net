﻿//-----------------------------------------------------------------------
// <copyright file="IUnmutableFilter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.TestKit
{
    // ReSharper disable once InconsistentNaming
    /// <summary>
    /// TBD
    /// </summary>
    public interface IUnmutableFilter : IDisposable
    {
        /// <summary>
        /// Call this to let events that previously have been muted to be logged again.
        /// </summary>
        void Unmute();
    }
}
