//-----------------------------------------------------------------------
// <copyright file="IDateTimeNowTimeProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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

