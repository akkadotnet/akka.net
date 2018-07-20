//-----------------------------------------------------------------------
// <copyright file="IKeyValuePair.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Util.Internal.Collections
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TKey">TBD</typeparam>
    /// <typeparam name="TValue">TBD</typeparam>
    public interface IKeyValuePair<out TKey, out TValue>
    {
        /// <summary>
        /// TBD
        /// </summary>
        TKey Key { get; }
        /// <summary>
        /// TBD
        /// </summary>
        TValue Value { get; }
    }
}

