//-----------------------------------------------------------------------
// <copyright file="EnvironmentHelper.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Reflection;

namespace Akka.Configuration
{
    /// <summary>
    /// Provide helpers to check the environment where Akka is currently running
    /// </summary>
    internal static class EnvironmentHelper
    {
        #region Ctor

        /// <summary>
        /// Initializes the <see cref="EnvironmentHelper"/> class.
        /// </summary>
        static EnvironmentHelper()
        {
            RuntimeNetCoreVersion = GetNetCoreVersion();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the runtime net core version.
        /// </summary>
        /// <remarks>
        ///     If the RuntimeNetCoreVersion is null the system akka is running on a .net Classic environment
        /// </remarks>
        public static string RuntimeNetCoreVersion { get; }

        #endregion

        #region Methods

        /// <summary>
        /// Gets the net core version.
        /// </summary>
        private static string GetNetCoreVersion()
        {
            var assembly = typeof(System.Runtime.GCSettings).GetTypeInfo().Assembly;
            var assemblyPath = assembly.CodeBase.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            int netCoreAppIndex = Array.IndexOf(assemblyPath, "Microsoft.NETCore.App");
            if (netCoreAppIndex > 0 && netCoreAppIndex < assemblyPath.Length - 2)
                return assemblyPath[netCoreAppIndex + 1];
            return null;
        }

        #endregion

    }
}
