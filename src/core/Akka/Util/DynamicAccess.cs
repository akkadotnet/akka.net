// //-----------------------------------------------------------------------
// // <copyright file="DynamicAccess.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;

namespace Akka.Util
{
    /// <summary>
    /// DynamicAccess
    /// </summary>
    public static class DynamicAccess
    {
        /// <summary>
        /// Creates instance of specified type name using reflection
        /// </summary>
        /// <remarks>
        /// Does mostly the same thing as <see cref="Activator"/> class, but makes conversion and error handling simpler
        /// </remarks>
        public static Try<TResult> CreateInstanceFor<TResult>(string typeName, params object[] args) where TResult : class
        {
            return Activator.CreateInstance(Type.GetType(typeName), args) as Try<TResult>;
        }
    }
}