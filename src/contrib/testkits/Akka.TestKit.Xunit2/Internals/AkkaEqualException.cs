//-----------------------------------------------------------------------
// <copyright file="AkkaEqualException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
using Xunit.Sdk;

#nullable enable
namespace Akka.TestKit.Xunit2.Internals
{
    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    public class AkkaEqualException : XunitException
    {
        // Length of "Expected: " and "Actual:   "
        private static readonly string NewLineAndIndent = Environment.NewLine + new string(' ', 10);
        
        public static AkkaEqualException ForMismatchedValues(
            object? expected,
            object? actual,
            string? format = null,
            params object[] args)
        {
            // Strings normally come through ForMismatchedStrings, so we want to make sure any
            // string value that comes through here isn't re-formatted/truncated. This is for
            // two reasons: (a) to support Assert.Equal<object>(string1, string2) to get a full
            // printout of the raw string values, which is useful when debugging; and (b) to
            // allow the assertion functions to pre-format the value themselves, perhaps with
            // additional information (like DateTime/DateTimeOffset when providing the precision
            // of the comparison).
            var expectedText = expected as string ?? ArgumentFormatter.Format(expected);
            var actualText = actual as string ?? ArgumentFormatter.Format(actual);

            return new AkkaEqualException(
                "Assert.Equal() Failure: " + (format ?? "Values differ") + Environment.NewLine +
                "Expected: " + expectedText.Replace(Environment.NewLine, NewLineAndIndent) + Environment.NewLine +
                "Actual:   " + actualText.Replace(Environment.NewLine, NewLineAndIndent),
                args
            );
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AkkaEqualException"/> class.
        /// </summary>
        /// <param name="format">A template string that describes the error.</param>
        /// <param name="args">An optional object array that contains zero or more objects to format.</param>
        public AkkaEqualException(string format = "", params object[] args)
            : base(BuildAssertionMessage(format, args)) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="AkkaEqualException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected AkkaEqualException(SerializationInfo info, StreamingContext context)
            : base(info.GetString("Message")) { }

        /// <summary>
        /// Builds assertion message by applying specified arguments to the format string.
        /// When no arguments are specified, format string is returned as-is.
        /// </summary>
        internal static string? BuildAssertionMessage(string format, object[] args)
        {
            if (string.IsNullOrEmpty(format))
            {
                return null;
            }

            if (args is not { Length: > 0 })
            {
                return format;
            }

            try
            {
                return string.Format(format, args);
            }
            catch (Exception)
            {
                return $"""[Could not string.Format("{format}", {string.Join(", ", args)})]""";
            }
        }
    }
}
