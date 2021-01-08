//-----------------------------------------------------------------------
// <copyright file="DynamicAccess.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Annotations;

namespace Akka.Util
{
    /// <summary>
    /// INTERNAL USAGE
    /// 
    /// DynamicAccess
    /// </summary>
    [InternalApi]
    public static class DynamicAccess
    {
        /// <summary>
        /// INTERNAL API
        /// 
        /// Creates instance of specified type name using reflection
        /// </summary>
        /// <remarks>
        /// Does mostly the same thing as <see cref="Activator"/> class, but makes conversion and error handling simpler
        /// </remarks>
        [InternalApi]
        public static Try<TResult> CreateInstanceFor<TResult>(string typeName, params object[] args) where TResult : class
        {
            try
            {
                return Activator.CreateInstance(Type.GetType(typeName), args) as TResult;
            }
            catch (Exception ex)
            {
                return new Try<TResult>(ex);
            }
        }
    }
}
