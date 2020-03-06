// //-----------------------------------------------------------------------
// // <copyright file="IHoconConfig.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Hocon
{
    public interface IHoconConfig
    {
        /// <summary>
        /// Determines if this root node contains any values
        /// </summary>
        bool IsEmpty { get; }

        /// <summary>
        /// Retrieves a boolean value from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the value to retrieve.</param>
        /// <param name="default">The default value to return if the value doesn't exist.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The boolean value defined in the specified path.</returns>
        bool GetBoolean(string path, bool @default = false);

        /// <summary>
        /// Retrieves a long value, optionally suffixed with a 'b', from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the value to retrieve.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The long value defined in the specified path.</returns>
        long? GetByteSize(string path);

        /// <summary>
        /// Retrieves a long value, optionally suffixed with a 'b', from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the value to retrieve.</param>
        /// <param name="def">Default return value if none provided.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The long value defined in the specified path.</returns>
        long? GetByteSize(string path, long? def = null);

        /// <summary>
        /// Retrieves an integer value from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the value to retrieve.</param>
        /// <param name="default">The default value to return if the value doesn't exist.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The integer value defined in the specified path.</returns>
        int GetInt(string path, int @default = 0);

        /// <summary>
        /// Retrieves a long value from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the value to retrieve.</param>
        /// <param name="default">The default value to return if the value doesn't exist.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The long value defined in the specified path.</returns>
        long GetLong(string path, long @default = 0);

        /// <summary>
        /// Retrieves a string value from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the value to retrieve.</param>
        /// <param name="default">The default value to return if the value doesn't exist.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The string value defined in the specified path.</returns>
        string GetString(string path, string @default = null);

        /// <summary>
        /// Retrieves a float value from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the value to retrieve.</param>
        /// <param name="default">The default value to return if the value doesn't exist.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The float value defined in the specified path.</returns>
        float GetFloat(string path, float @default = 0);

        /// <summary>
        /// Retrieves a decimal value from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the value to retrieve.</param>
        /// <param name="default">The default value to return if the value doesn't exist.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The decimal value defined in the specified path.</returns>
        decimal GetDecimal(string path, decimal @default = 0);

        /// <summary>
        /// Retrieves a double value from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the value to retrieve.</param>
        /// <param name="default">The default value to return if the value doesn't exist.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The double value defined in the specified path.</returns>
        double GetDouble(string path, double @default = 0);

        /// <summary>
        /// Retrieves a list of boolean values from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the values to retrieve.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The list of boolean values defined in the specified path.</returns>
        IList<Boolean> GetBooleanList(string path);

        /// <summary>
        /// Retrieves a list of decimal values from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the values to retrieve.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The list of decimal values defined in the specified path.</returns>
        IList<decimal> GetDecimalList(string path);

        /// <summary>
        /// Retrieves a list of float values from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the values to retrieve.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The list of float values defined in the specified path.</returns>
        IList<float> GetFloatList(string path);

        /// <summary>
        /// Retrieves a list of double values from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the values to retrieve.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The list of double values defined in the specified path.</returns>
        IList<double> GetDoubleList(string path);

        /// <summary>
        /// Retrieves a list of int values from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the values to retrieve.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The list of int values defined in the specified path.</returns>
        IList<int> GetIntList(string path);

        /// <summary>
        /// Retrieves a list of long values from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the values to retrieve.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The list of long values defined in the specified path.</returns>
        IList<long> GetLongList(string path);

        /// <summary>
        /// Retrieves a list of byte values from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the values to retrieve.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The list of byte values defined in the specified path.</returns>
        IList<byte> GetByteList(string path);

        /// <summary>
        /// Retrieves a list of string values from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the values to retrieve.</param>
        /// <param name="strings"></param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The list of string values defined in the specified path.</returns>
        IList<string> GetStringList(string path);

        /// <summary>
        /// Retrieves a list of string values from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the values to retrieve.</param>
        /// <param name="defaultPaths">Default paths that will be returned to the user.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The list of string values defined in the specified path.</returns>
        IList<string> GetStringList(string path, string[] defaultPaths);

        /// <summary>
        /// Retrieves a new configuration from the current configuration
        /// with the root node being the supplied path.
        /// </summary>
        /// <param name="path">The path that contains the configuration to retrieve.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>A new configuration with the root node being the supplied path.</returns>
        IHoconConfig GetConfig(string path);

        /// <summary>
        /// Retrieves a <see cref="TimeSpan"/> value from the specified path in the configuration.
        /// </summary>
        /// <param name="path">The path that contains the value to retrieve.</param>
        /// <param name="default">The default value to return if the value doesn't exist.</param>
        /// <param name="allowInfinite"><c>true</c> if infinite timespans are allowed; otherwise <c>false</c>.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns>The <see cref="TimeSpan"/> value defined in the specified path.</returns>
        TimeSpan GetTimeSpan(string path, TimeSpan? @default = null, bool allowInfinite = true);

        /// <summary>
        /// Converts the current configuration to a string.
        /// </summary>
        /// <returns>A string containing the current configuration.</returns>
        string ToString();

        /// <summary>
        /// Converts the current configuration to a string 
        /// </summary>
        /// <param name="includeFallback">if true returns string with current config combined with fallback key-values else only current config key-values</param>
        /// <returns>TBD</returns>
        string ToString(bool includeFallback);

        /// <summary>
        /// Configure the current configuration with a secondary source.
        /// </summary>
        /// <param name="fallback">The configuration to use as a secondary source.</param>
        /// <exception cref="ArgumentException">This exception is thrown if the given <paramref name="fallback"/> is a reference to this instance.</exception>
        /// <returns>The current configuration configured with the specified fallback.</returns>
        IHoconConfig WithFallback(IHoconConfig fallback);

        /// <summary>
        /// Determine if a HOCON configuration element exists at the specified location
        /// </summary>
        /// <param name="path">The location to check for a configuration value.</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the current node is undefined.</exception>
        /// <returns><c>true</c> if a value was found, <c>false</c> otherwise.</returns>
        bool HasPath(string path);
    }
}