//-----------------------------------------------------------------------
// <copyright file="SemanticLogMessageFormatter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Akka.Event
{
    /// <summary>
    /// Message formatter that supports semantic logging with both positional and named templates.
    /// Supports Serilog-style message templates like "User {UserId} logged in from {IpAddress}".
    /// Also supports traditional positional templates like "Value is {0} and status {1}".
    /// </summary>
    public sealed class SemanticLogMessageFormatter : ILogMessageFormatter
    {
        /// <summary>
        /// Gets the singleton instance of the <see cref="SemanticLogMessageFormatter"/>.
        /// </summary>
        public static readonly SemanticLogMessageFormatter Instance = new();

        private SemanticLogMessageFormatter()
        {
        }

        /// <summary>
        /// Formats a log message using the specified format string and arguments.
        /// </summary>
        /// <param name="format">The format string (supports both {0} and {PropertyName} styles)</param>
        /// <param name="args">The arguments to format</param>
        /// <returns>The formatted message string</returns>
        public string Format(string format, params object[] args)
        {
            return Format(format, (IEnumerable<object>)args);
        }

        /// <summary>
        /// Formats a log message using the specified format string and arguments.
        /// </summary>
        /// <param name="format">The format string (supports both {0} and {PropertyName} styles)</param>
        /// <param name="args">The arguments to format</param>
        /// <returns>The formatted message string</returns>
        public string Format(string format, IEnumerable<object> args)
        {
            if (string.IsNullOrEmpty(format))
                return string.Empty;

            var argArray = args?.ToArray() ?? Array.Empty<object>();
            if (argArray.Length == 0)
                return format;

            // Get property names from the template
            var propertyNames = MessageTemplateParser.GetPropertyNames(format);
            if (propertyNames.Count == 0)
                return format;

            // Check if this is a positional template or named template
            var isPositional = propertyNames.Count > 0 && int.TryParse(propertyNames[0], out _);

            if (isPositional)
            {
                // Use standard string.Format for positional templates
                try
                {
                    return string.Format(format, argArray);
                }
                catch (FormatException)
                {
                    // If formatting fails, return the format string with args appended
                    return $"{format} [{string.Join(", ", argArray)}]";
                }
            }
            else
            {
                // Named template - do semantic substitution
                return FormatNamedTemplate(format, propertyNames, argArray);
            }
        }

        /// <summary>
        /// Formats a named template by replacing {PropertyName} with values.
        /// </summary>
        private static string FormatNamedTemplate(string format, IReadOnlyList<string> propertyNames, object[] args)
        {
            var result = new StringBuilder(format.Length + args.Length * 10);
            var length = format.Length;
            var i = 0;
            var argIndex = 0;

            while (i < length)
            {
                var openBrace = format.IndexOf('{', i);
                if (openBrace == -1)
                {
                    // No more placeholders, append rest of string
                    result.Append(format.Substring(i));
                    break;
                }

                // Append everything before the placeholder
                result.Append(format.Substring(i, openBrace - i));

                // Check for escaped brace {{
                if (openBrace + 1 < length && format[openBrace + 1] == '{')
                {
                    result.Append('{');
                    i = openBrace + 2;
                    continue;
                }

                var closeBrace = format.IndexOf('}', openBrace + 1);
                if (closeBrace == -1)
                {
                    // Malformed template, append rest and break
                    result.Append(format.Substring(openBrace));
                    break;
                }

                // Check for escaped brace }}
                if (closeBrace + 1 < length && format[closeBrace + 1] == '}')
                {
                    result.Append('}');
                    i = closeBrace + 2;
                    continue;
                }

                // Extract the placeholder content
                var placeholderLength = closeBrace - openBrace - 1;
                if (placeholderLength > 0)
                {
                    var placeholder = format.Substring(openBrace + 1, placeholderLength).Trim();

                    // Remove format specifiers (e.g., {Value:N2} -> Value)
                    var colonIndex = placeholder.IndexOf(':');
                    string formatSpec = null;
                    if (colonIndex > 0)
                    {
                        formatSpec = placeholder.Substring(colonIndex + 1);
                        placeholder = placeholder.Substring(0, colonIndex).Trim();
                    }

                    // Remove alignment specifiers (e.g., {Value,10} -> Value)
                    var commaIndex = placeholder.IndexOf(',');
                    if (commaIndex > 0)
                    {
                        placeholder = placeholder.Substring(0, commaIndex).Trim();
                    }

                    // Substitute the value
                    if (argIndex < args.Length)
                    {
                        var value = args[argIndex];
                        if (value != null)
                        {
                            // Apply format specifier if present
                            if (!string.IsNullOrEmpty(formatSpec))
                            {
                                try
                                {
                                    result.Append(string.Format($"{{0:{formatSpec}}}", value));
                                }
                                catch
                                {
                                    // If formatting fails, just use ToString()
                                    result.Append(value.ToString());
                                }
                            }
                            else
                            {
                                result.Append(value.ToString());
                            }
                        }
                        else
                        {
                            result.Append("null");
                        }
                        argIndex++;
                    }
                    else
                    {
                        // Not enough args, keep the placeholder
                        result.Append('{').Append(placeholder).Append('}');
                    }
                }

                i = closeBrace + 1;
            }

            return result.ToString();
        }
    }
}
