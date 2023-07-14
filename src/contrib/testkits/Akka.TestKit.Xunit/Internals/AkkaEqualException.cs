//-----------------------------------------------------------------------
// <copyright file="AkkaEqualException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
using Xunit.Sdk;

namespace Akka.TestKit.Xunit2.Internals
{
    /// <summary>
    /// TBD
    /// </summary>
    public class AkkaEqualException : XunitException
    {
        private static readonly string NewLineAndIndent = Environment.NewLine + new string(' ', 10);  // Length of "Expected: " and "Actual:   "
        
        private readonly string _format;
        private readonly object[] _args;

        public static AkkaEqualException ForMismatchedValues(
            object expected,
            object actual,
            string format = null,
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
                @$"Assert.Equal() Failure: {format ?? "Values differ"}{Environment.NewLine}
Expected: {expectedText.Replace(Environment.NewLine, NewLineAndIndent)}{Environment.NewLine}
Actual:   {actualText.Replace(Environment.NewLine, NewLineAndIndent)}",
                args
            );

        }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="AkkaEqualException"/> class.
        /// </summary>
        /// <param name="format">A template string that describes the error.</param>
        /// <param name="args">An optional object array that contains zero or more objects to format.</param>
        public AkkaEqualException(string format = "", params object[] args): base(null)
        {
            _format = format;
            _args = args;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AkkaEqualException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected AkkaEqualException(SerializationInfo info, StreamingContext context): base(null)
        {
        }

        /// <summary>
        /// The message that describes the error.
        /// </summary>
        public override string Message
        {
            get
            {
                if(string.IsNullOrEmpty(_format))
                    return base.Message;

                string message;
                try
                {
                    message = string.Format(_format, _args);
                }
                catch(Exception)
                {
                    message = $@"[Could not string.Format(""{_format}"", {string.Join(", ", _args)})]";
                }

                return base.Message is not null ? $"{base.Message} {message}" : message;
            }
        }
    }
}
