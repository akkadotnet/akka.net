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

        public ErrorFilter(IStringMatcher messageMatcher = null, IStringMatcher sourceMatcher = null)
            : this(null,messageMatcher,sourceMatcher)
        {
        }

        public ErrorFilter(Type exceptionType, IStringMatcher messageMatcher = null, IStringMatcher sourceMatcher = null)
            : base(messageMatcher,sourceMatcher)
        {
            if(exceptionType!=null && !exceptionType.Implements<Exception>()) throw new ArgumentException("The type must be an exception. It was: " + exceptionType, "exceptionType");
            _exceptionType = exceptionType;
        }

        protected override bool IsMatch(LogEvent evt)
        {
            var error = evt as Error;
            if(error != null)
            {
                var cause = error.Cause;
                var hasCause = cause != null;
                var matchAnyErrorType = _exceptionType == null;
                if(matchAnyErrorType)
                {
                    if(!hasCause)
                    {
                        return InternalDoMatch(error.LogSource, error.Message);
                    }
                }
                else
                {
                    if(!hasCause || !cause.GetType().Implements(_exceptionType))
                        return false;
                }
                var causeMessage = cause.Message;
                var logSource = error.LogSource;
                var errorMessage = error.Message;
                return (errorMessage == null && string.IsNullOrEmpty(causeMessage) && string.IsNullOrEmpty(cause.StackTrace))
                       || InternalDoMatch(logSource, errorMessage)
                       || InternalDoMatch(logSource, causeMessage);
            }

            return false;
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