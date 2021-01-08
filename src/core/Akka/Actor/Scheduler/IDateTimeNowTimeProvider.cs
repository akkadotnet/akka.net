//-----------------------------------------------------------------------
// <copyright file="IDateTimeNowTimeProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor
{
    /// <summary>
    /// Marks that an <see cref="ITimeProvider"/> uses <see cref="DateTimeOffset.UtcNow"/>, 
    /// i.e. system time, to provide <see cref="ITimeProvider.Now"/>.
    /// </summary>
    public interface IDateTimeOffsetNowTimeProvider : ITimeProvider
    { }
}

