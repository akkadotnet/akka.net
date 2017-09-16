//-----------------------------------------------------------------------
// <copyright file="RuntimeDetector.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Util
{
    /// <summary>
    /// Used to detect specific .NET runtimes, to make it easier to adjust for platform specific
    /// differences.
    /// </summary>
    /// <remarks>
    /// Mostly used for detecting Mono right now because certain features, i.e. IPV6 support, aren't
    /// fully supported on it. Can also be used for picking platform-specific implementations of things
    /// such as Akka.Cluster.Metrics implementations.
    /// </remarks>
    public static class RuntimeDetector
    {
        /// <summary>
        /// Is <c>true</c> if we're running on a Mono VM. <c>false</c> otherwise.
        /// </summary>
        public static readonly bool IsMono = Type.GetType("Mono.Runtime") != null;
    }
}

