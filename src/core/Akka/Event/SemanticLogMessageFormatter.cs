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
    /// Implements the <see href="https://messagetemplates.org/">Message Templates</see> specification,
    /// which is the language-neutral standard used by Serilog, Microsoft.Extensions.Logging, NLog,
    /// and other structured logging frameworks.
    /// </summary>
    /// <remarks>
    /// <para>Supported syntax:</para>
    /// <list type="bullet">
    ///   <item><description>Named properties: <c>{PropertyName}</c></description></item>
    ///   <item><description>Positional properties: <c>{0}</c>, <c>{1}</c></description></item>
    ///   <item><description>Format specifiers: <c>{Value:N2}</c>, <c>{Date:yyyy-MM-dd}</c></description></item>
    ///   <item><description>Alignment: <c>{Value,10}</c>, <c>{Value,-10}</c></description></item>
    ///   <item><description>Escaped braces: <c>{{</c> → <c>{</c>, <c>}}</c> → <c>}</c></description></item>
    /// </list>
    /// <para>Not supported:</para>
    /// <list type="bullet">
    ///   <item><description>Destructuring operators: <c>{@Object}</c>, <c>{$Object}</c> (Serilog-specific)</description></item>
    ///   <item><description>Empty property names: <c>{:N2}</c> (invalid per spec)</description></item>
    /// </list>
    /// <para><b>Null handling:</b> Named templates (e.g., <c>{PropertyName}</c>) render <c>null</c> values
    /// as the literal string <c>"null"</c>. This is consistent with the Message Templates specification
    /// and matches the behavior of Serilog, NLog, and other structured logging frameworks.
    /// In contrast, positional templates (e.g., <c>{0}</c>) delegate to <see cref="string.Format(string, object[])"/>,
    /// which renders <c>null</c> as an empty string <c>""</c>. This asymmetry is by design:
    /// positional templates preserve backward compatibility with <see cref="DefaultLogMessageFormatter"/>,
    /// while named templates follow the Message Templates spec.</para>
    /// </remarks>
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

            // Optimize: avoid ToArray() if args is already an array or IReadOnlyList
            object[] argArray;
            if (args == null)
            {
                argArray = Array.Empty<object>();
            }
            else if (args is object[] array)
            {
                argArray = array;
            }
            else if (args is IReadOnlyList<object> readOnlyList)
            {
                // LogValues<T> structs implement IReadOnlyList<object>, use them directly
                // Only convert to array for string.Format which requires object[]
                var propertyNames = MessageTemplateParser.GetPropertyNames(format);
                if (propertyNames.Count == 0)
                    return UnescapeBraces(format);

                var isPositional = propertyNames.Count > 0 && int.TryParse(propertyNames[0], out _);

                if (isPositional)
                {
                    // string.Format requires object[], so convert here only
                    argArray = new object[readOnlyList.Count];
                    for (int i = 0; i < readOnlyList.Count; i++)
                        argArray[i] = readOnlyList[i];

                    // For positional templates, use string.Format directly without catching FormatException
                    // to maintain backward compatibility with DefaultLogMessageFormatter behavior
                    return string.Format(format, argArray);
                }
                else
                {
                    // Named template - use IReadOnlyList directly
                    return FormatNamedTemplate(format, propertyNames, readOnlyList);
                }
            }
            else
            {
                argArray = args.ToArray();
            }

            if (argArray.Length == 0)
                return UnescapeBraces(format);

            // Get property names from the template
            var propertyNames2 = MessageTemplateParser.GetPropertyNames(format);
            if (propertyNames2.Count == 0)
                return UnescapeBraces(format);

            // Check if this is a positional template or named template
            var isPositional2 = propertyNames2.Count > 0 && int.TryParse(propertyNames2[0], out _);

            if (isPositional2)
            {
                // For positional templates, use string.Format directly without catching FormatException
                // to maintain backward compatibility with DefaultLogMessageFormatter behavior
                return string.Format(format, argArray);
            }
            else
            {
                // Named template - do semantic substitution
                return FormatNamedTemplate(format, propertyNames2, argArray);
            }
        }

        /// <summary>
        /// Unescapes {{ to { and }} to } in a string that has no placeholders.
        /// </summary>
        private static string UnescapeBraces(string format)
        {
            // Fast path: if no escaped braces, return as-is
            if (format.IndexOf('{') == -1 && format.IndexOf('}') == -1)
                return format;

            var result = new StringBuilder(format.Length);
            var length = format.Length;
            var i = 0;

            while (i < length)
            {
                var ch = format[i];

                if (ch == '{' && i + 1 < length && format[i + 1] == '{')
                {
                    result.Append('{');
                    i += 2;
                }
                else if (ch == '}' && i + 1 < length && format[i + 1] == '}')
                {
                    result.Append('}');
                    i += 2;
                }
                else
                {
                    result.Append(ch);
                    i++;
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// Formats a named template by replacing {PropertyName} with values.
        /// Handles escaped braces: {{ → {, }} → }
        /// </summary>
        private static string FormatNamedTemplate(string format, IReadOnlyList<string> propertyNames, IReadOnlyList<object> args)
        {
            var result = new StringBuilder(format.Length + args.Count * 10);
            var length = format.Length;
            var i = 0;
            var argIndex = 0;

            while (i < length)
            {
                var ch = format[i];

                // Check for escaped }} in literal text
                if (ch == '}' && i + 1 < length && format[i + 1] == '}')
                {
                    result.Append('}');
                    i += 2;
                    continue;
                }

                // Check for placeholder start
                if (ch == '{')
                {
                    // Check for escaped brace {{
                    if (i + 1 < length && format[i + 1] == '{')
                    {
                        result.Append('{');
                        i += 2;
                        continue;
                    }

                    // Find closing brace for placeholder
                    var closeBrace = format.IndexOf('}', i + 1);
                    if (closeBrace == -1)
                    {
                        // Malformed template, append rest and break
                        result.Append(format.Substring(i));
                        break;
                    }

                    // Extract the placeholder content
                    var placeholderLength = closeBrace - i - 1;
                    if (placeholderLength > 0)
                    {
                        var placeholder = format.Substring(i + 1, placeholderLength).Trim();

                        // Parse placeholder: {Name,alignment:format}
                        // First, find the property name (before comma or colon)
                        var commaIndex = placeholder.IndexOf(',');
                        var colonIndex = placeholder.IndexOf(':');
                        string propertyName;
                        string alignmentSpec = null;
                        string formatSpec = null;

                        // Determine the property name endpoint
                        var endOfName = placeholder.Length;
                        if (commaIndex >= 0 && (colonIndex < 0 || commaIndex < colonIndex))
                        {
                            // Comma comes first (or no colon)
                            endOfName = commaIndex;
                        }
                        else if (colonIndex >= 0)
                        {
                            // Colon comes first (or no comma)
                            endOfName = colonIndex;
                        }

                        propertyName = placeholder.Substring(0, endOfName).Trim();

                        // Extract alignment if present
                        if (commaIndex >= 0)
                        {
                            var alignmentStart = commaIndex + 1;
                            var alignmentEnd = colonIndex >= 0 ? colonIndex : placeholder.Length;
                            alignmentSpec = placeholder.Substring(alignmentStart, alignmentEnd - alignmentStart).Trim();
                        }

                        // Extract format specifier if present
                        if (colonIndex >= 0)
                        {
                            formatSpec = placeholder.Substring(colonIndex + 1).Trim();
                        }

                        placeholder = propertyName;

                        // Substitute the value
                        if (argIndex < args.Count)
                        {
                            var value = args[argIndex];
                            string formattedValue;

                            if (value != null)
                            {
                                // First get the string representation
                                var strValue = value.ToString();

                                if (strValue != null)
                                {
                                    // Apply format specifier if present
                                    if (!string.IsNullOrEmpty(formatSpec))
                                    {
                                        try
                                        {
                                            formattedValue = string.Format($"{{0:{formatSpec}}}", value);
                                        }
                                        catch
                                        {
                                            // If formatting fails, use the plain string
                                            formattedValue = strValue;
                                        }
                                    }
                                    else
                                    {
                                        formattedValue = strValue;
                                    }
                                }
                                else
                                {
                                    // ToString() returned null
                                    formattedValue = "null";
                                }
                            }
                            else
                            {
                                formattedValue = "null";
                            }

                            // Apply alignment if present
                            if (!string.IsNullOrEmpty(alignmentSpec) && int.TryParse(alignmentSpec, out var alignment))
                            {
                                formattedValue = alignment > 0
                                    ? formattedValue.PadLeft(alignment)
                                    : formattedValue.PadRight(-alignment);
                            }

                            result.Append(formattedValue);
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
                else
                {
                    // Regular character
                    result.Append(ch);
                    i++;
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// Formats a named template by replacing {PropertyName} with values.
        /// Handles escaped braces: {{ → {, }} → }
        /// </summary>
        private static string FormatNamedTemplate(string format, IReadOnlyList<string> propertyNames, object[] args)
        {
            var result = new StringBuilder(format.Length + args.Length * 10);
            var length = format.Length;
            var i = 0;
            var argIndex = 0;

            while (i < length)
            {
                var ch = format[i];

                // Check for escaped }} in literal text
                if (ch == '}' && i + 1 < length && format[i + 1] == '}')
                {
                    result.Append('}');
                    i += 2;
                    continue;
                }

                // Check for placeholder start
                if (ch == '{')
                {
                    // Check for escaped brace {{
                    if (i + 1 < length && format[i + 1] == '{')
                    {
                        result.Append('{');
                        i += 2;
                        continue;
                    }

                    // Find closing brace for placeholder
                    var closeBrace = format.IndexOf('}', i + 1);
                    if (closeBrace == -1)
                    {
                        // Malformed template, append rest and break
                        result.Append(format.Substring(i));
                        break;
                    }

                    // Extract the placeholder content
                    var placeholderLength = closeBrace - i - 1;
                    if (placeholderLength > 0)
                    {
                        var placeholder = format.Substring(i + 1, placeholderLength).Trim();

                        // Parse placeholder: {Name,alignment:format}
                        // First, find the property name (before comma or colon)
                        var commaIndex = placeholder.IndexOf(',');
                        var colonIndex = placeholder.IndexOf(':');
                        string propertyName;
                        string alignmentSpec = null;
                        string formatSpec = null;

                        // Determine the property name endpoint
                        var endOfName = placeholder.Length;
                        if (commaIndex >= 0 && (colonIndex < 0 || commaIndex < colonIndex))
                        {
                            // Comma comes first (or no colon)
                            endOfName = commaIndex;
                        }
                        else if (colonIndex >= 0)
                        {
                            // Colon comes first (or no comma)
                            endOfName = colonIndex;
                        }

                        propertyName = placeholder.Substring(0, endOfName).Trim();

                        // Extract alignment if present
                        if (commaIndex >= 0)
                        {
                            var alignmentStart = commaIndex + 1;
                            var alignmentEnd = colonIndex >= 0 ? colonIndex : placeholder.Length;
                            alignmentSpec = placeholder.Substring(alignmentStart, alignmentEnd - alignmentStart).Trim();
                        }

                        // Extract format specifier if present
                        if (colonIndex >= 0)
                        {
                            formatSpec = placeholder.Substring(colonIndex + 1).Trim();
                        }

                        placeholder = propertyName;

                        // Substitute the value
                        if (argIndex < args.Length)
                        {
                            var value = args[argIndex];
                            string formattedValue;

                            if (value != null)
                            {
                                // First get the string representation
                                var strValue = value.ToString();

                                if (strValue != null)
                                {
                                    // Apply format specifier if present
                                    if (!string.IsNullOrEmpty(formatSpec))
                                    {
                                        try
                                        {
                                            formattedValue = string.Format($"{{0:{formatSpec}}}", value);
                                        }
                                        catch
                                        {
                                            // If formatting fails, use the plain string
                                            formattedValue = strValue;
                                        }
                                    }
                                    else
                                    {
                                        formattedValue = strValue;
                                    }
                                }
                                else
                                {
                                    // ToString() returned null
                                    formattedValue = "null";
                                }
                            }
                            else
                            {
                                formattedValue = "null";
                            }

                            // Apply alignment if present
                            if (!string.IsNullOrEmpty(alignmentSpec) && int.TryParse(alignmentSpec, out var alignment))
                            {
                                formattedValue = alignment > 0
                                    ? formattedValue.PadLeft(alignment)
                                    : formattedValue.PadRight(-alignment);
                            }

                            result.Append(formattedValue);
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
                else
                {
                    // Regular character
                    result.Append(ch);
                    i++;
                }
            }

            return result.ToString();
        }
    }
}
