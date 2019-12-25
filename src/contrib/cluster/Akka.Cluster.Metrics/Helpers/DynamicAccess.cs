// //-----------------------------------------------------------------------
// // <copyright file="DynamicAccess.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;

namespace Akka.Cluster.Metrics.Helpers
{
    /// <summary>
    /// DynamicAccess
    /// </summary>
    internal static class DynamicAccess
    {
        /// <summary>
        /// Creates instance of specified type name using reflection
        /// </summary>
        public static Try<TResult> CreateInstanceFor<TResult>(string typeName, params object[] args) where TResult : class
        {
            return Activator.CreateInstance(Type.GetType(typeName), args) as Try<TResult>;
        }
    }
}