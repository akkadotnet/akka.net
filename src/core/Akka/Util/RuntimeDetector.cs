//-----------------------------------------------------------------------
// <copyright file="RuntimeDetector.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

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

        /// <summary>
        /// Is <c>true</c> if we've detected Windows as a platform.
        /// </summary>
        public static readonly bool IsWindows = _IsWindows();

        /// <summary>
        /// Private implementation method not meant for public consumption
        /// </summary>
        /// <returns><c>true</c> if the current runtime is Windows</returns>
        private static bool _IsWindows()
        {
#if CORECLR
            return System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#else
            return System.Environment.OSVersion.Platform != PlatformID.MacOSX &&
                   System.Environment.OSVersion.Platform != PlatformID.Unix;
#endif
        }
    }
}

