//-----------------------------------------------------------------------
// <copyright file="Construct.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Streams
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class Construct
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="genericType">TBD</param>
        /// <param name="genericParam">TBD</param>
        /// <param name="constructorArgs">TBD</param>
        /// <returns>TBD</returns>
        public static object Instantiate(this Type genericType, Type genericParam, params object[] constructorArgs)
        {
            var gen = genericType.MakeGenericType(genericParam);
            return Activator.CreateInstance(gen, constructorArgs);
        }
    }
}