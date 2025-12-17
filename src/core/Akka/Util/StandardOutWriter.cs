//-----------------------------------------------------------------------
// <copyright file="StandardOutWriter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;

namespace Akka.Util
{

    /// <summary>
    /// This class contains methods for thread safe writing to the standard output stream.
    ///  </summary>
    public static class StandardOutWriter
    {
        private static readonly object _lock = new();
        private static readonly bool _isConsoleAvailable = DetectConsoleAvailability();

        /// <summary>
        /// Detects whether a real console is available for output.
        /// In environments like IIS and Windows Services, console output is redirected to StreamWriter.Null,
        /// which is a singleton. When multiple threads write to both Console.Out and Console.Error
        /// (which point to the same StreamWriter.Null instance), it causes race conditions.
        /// 
        /// Since console output goes nowhere in these environments anyway, we skip it entirely
        /// to prevent the race condition and improve performance.
        /// </summary>
        private static bool DetectConsoleAvailability()
        {
            // Specifically detect the IIS/Windows Service scenario where both Console.Out 
            // and Console.Error point to the SAME StreamWriter.Null singleton instance.
            // This is the exact condition that causes the race condition.
            // Note: We check both because in these environments, both are always set to the same instance
            if (Console.Out == StreamWriter.Null && Console.Error == StreamWriter.Null)
                return false;
            
            // Also check Environment.UserInteractive for additional safety
            // This returns false for Windows Services and IIS in .NET Framework
            // (though less reliable in .NET Core, the StreamWriter.Null check above is the key)
            if (!Environment.UserInteractive)
                return false;
                
            return true;
        }

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
            // Skip console output in IIS, Windows Services, and other non-console environments.
            // In these environments:
            // 1. Console output is redirected to StreamWriter.Null (goes nowhere anyway)
            // 2. Both Console.Out and Console.Error point to the same StreamWriter.Null singleton
            // 3. Concurrent writes to both streams cause race conditions and IndexOutOfRangeException
            // 4. Skipping output entirely prevents the race condition and improves performance
            // See: https://github.com/akkadotnet/akka.net/issues/7691
            if (!_isConsoleAvailable)
                return;

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
