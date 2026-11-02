//-----------------------------------------------------------------------
// <copyright file="IAdvancedScheduler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor
{
    /// <summary>
    /// Extended scheduler interface that supports scheduling arbitrary <see cref="System.Action"/> delegates.
    /// Access via <see cref="IScheduler.Advanced"/>.
    /// </summary>
    public interface IAdvancedScheduler : IActionScheduler
    {
    }
}

