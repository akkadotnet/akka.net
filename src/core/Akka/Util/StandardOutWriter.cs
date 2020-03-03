//-----------------------------------------------------------------------
// <copyright file="StandardOutWriter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Util
{

    /// <summary>
    /// This class contains methods for thread safe writing to the standard output stream.
    ///  </summary>
    public static class StandardOutWriter
    {
        private static readonly object _lock = new object();

        /// <summary>
        /// Writes the specified <see cref="string"/> value to the standard output stream. Optionally 
        /// you may specify which colors should be used.
        /// </summary>
        /// <param name="message">The <see cref="string"/> value to write</param>
        /// <param name="foregroundColor">Optional: The foreground color</param>
        /// <param name="backgroundColor">Optional: The background color</param>
        public static void Write(string message, ConsoleColor? foregroundColor = null,
            ConsoleColor? backgroundColor = null)
        {
            WriteToConsole(message, foregroundColor, backgroundColor, false);
        }

        /// <summary>
        /// Writes the specified <see cref="string"/> value, followed by the current line terminator,
        /// to the standard output stream. Optionally you may specify which colors should be used.
        /// </summary>
        /// <param name="message">The <see cref="string"/> value to write</param>
        /// <param name="foregroundColor">Optional: The foreground color</param>
        /// <param name="backgroundColor">Optional: The background color</param>
        public static void WriteLine(string message, ConsoleColor? foregroundColor = null,
            ConsoleColor? backgroundColor = null)
        {
            WriteToConsole(message, foregroundColor, backgroundColor);
        }

        private static void WriteToConsole(string message, ConsoleColor? foregroundColor = null,
            ConsoleColor? backgroundColor = null, bool line = true)
        {
            lock (_lock)
            {
                ConsoleColor? fg = null;
                if (foregroundColor.HasValue)
                {
                    fg = Console.ForegroundColor;
                    Console.ForegroundColor = foregroundColor.Value;
                }
                ConsoleColor? bg = null;
                if (backgroundColor.HasValue)
                {
                    bg = Console.BackgroundColor;
                    Console.BackgroundColor = backgroundColor.Value;
                }
                if (line)
                    Console.WriteLine(message);
                else
                    Console.Write(message);
                if (fg.HasValue)
                {
                    Console.ForegroundColor = fg.Value;
                }
                if (bg.HasValue)
                {
                    Console.BackgroundColor = bg.Value;
                }
            }
        }
    }
}
