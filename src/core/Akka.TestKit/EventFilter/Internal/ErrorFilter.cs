//-----------------------------------------------------------------------
// <copyright file="ErrorFilter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Event;
using Akka.TestKit.Internal.StringMatcher;
using Akka.Util;

namespace Akka.TestKit.Internal
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class ErrorFilter : EventFilterBase
    {
        private readonly Type _exceptionType;
        private readonly bool _recurseInnerExceptions;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="messageMatcher">TBD</param>
        /// <param name="sourceMatcher">TBD</param>
        public ErrorFilter(IStringMatcher messageMatcher = null, IStringMatcher sourceMatcher = null)
            : this(null,messageMatcher,sourceMatcher, false)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="exceptionType">TBD</param>
        /// <param name="messageMatcher">TBD</param>
        /// <param name="sourceMatcher">TBD</param>
        /// <param name="recurseInnerExceptions">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="exceptionType"/> does not implement <see cref="Exception"/>.
        /// </exception>
        public ErrorFilter(Type exceptionType, IStringMatcher messageMatcher = null, IStringMatcher sourceMatcher = null, bool recurseInnerExceptions=false)
            : base(messageMatcher,sourceMatcher)
        {
            if(exceptionType!=null && !exceptionType.Implements<Exception>()) throw new ArgumentException($"The type must be an exception. It was: {exceptionType}", nameof(exceptionType));
            _exceptionType = exceptionType;
            _recurseInnerExceptions = recurseInnerExceptions;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="evt">TBD</param>
        /// <returns>TBD</returns>
        protected override bool IsMatch(LogEvent evt)
        {
            var error = evt as Error;
            if(error != null)
            {
                var logSource = error.LogSource;
                var errorMessage = error.Message;
                var cause = error.Cause;
                return IsMatch(logSource, errorMessage, cause);
            }

            return false;
        }

        private bool IsMatch(string logSource, object errorMessage, Exception cause)
        {
            var hasCause = cause != null;
            var matchAnyErrorType = _exceptionType == null;
            if (matchAnyErrorType)
            {
                if (!hasCause)
                {
                    return InternalDoMatch(logSource, errorMessage);
                }
            }
            else
            {
                //Must match type. If no cause, then it does not match the specified exception type
                if (!hasCause)
                {
                    return false;
                }
                if (!cause.GetType().Implements(_exceptionType))
                {
                    //The cause did not implement the specified type.
                    if (_recurseInnerExceptions)
                        return IsMatch(logSource, errorMessage, cause.InnerException);
                    return false;
                }
            }
            //At this stage we have a cause, and it's of the specified type.
            //Check that message matches, if nothing matches, recurse
            var causeMessage = cause.Message;
            var noMessages = (errorMessage == null && string.IsNullOrEmpty(causeMessage));
            return noMessages
                   || InternalDoMatch(logSource, errorMessage)
                   || InternalDoMatch(logSource, causeMessage)
                   || (_recurseInnerExceptions && IsMatch(logSource, errorMessage, cause.InnerException));
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override string FilterDescriptiveName
        {
            get
            {
                if(_exceptionType == null)
                    return "Error";
                return "Error with Cause <" + _exceptionType + ">";
            }
        }
    }
}
