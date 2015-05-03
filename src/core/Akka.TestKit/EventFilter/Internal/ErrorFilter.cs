﻿//-----------------------------------------------------------------------
// <copyright file="ErrorFilter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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

        public ErrorFilter(IStringMatcher messageMatcher = null, IStringMatcher sourceMatcher = null)
            : this(null,messageMatcher,sourceMatcher, false)
        {
        }

        public ErrorFilter(Type exceptionType, IStringMatcher messageMatcher = null, IStringMatcher sourceMatcher = null, bool recurseInnerExceptions=false)
            : base(messageMatcher,sourceMatcher)
        {
            if(exceptionType!=null && !exceptionType.Implements<Exception>()) throw new ArgumentException("The type must be an exception. It was: " + exceptionType, "exceptionType");
            _exceptionType = exceptionType;
            _recurseInnerExceptions = recurseInnerExceptions;
        }

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

