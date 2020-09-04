//-----------------------------------------------------------------------
// <copyright file="AkkaEqualException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
using Xunit.Sdk;

namespace Akka.TestKit.Xunit.Internals
{
    /// <summary>
    /// TBD
    /// </summary>
    public class AkkaEqualException : EqualException
    {
        private readonly string _format;
        private readonly object[] _args;

        /// <summary>
        /// Initializes a new instance of the <see cref="AkkaEqualException"/> class.
        /// </summary>
        /// <param name="expected">The expected value of the object</param>
        /// <param name="actual">The actual value of the object</param>
        /// <param name="format">A template string that describes the error.</param>
        /// <param name="args">An optional object array that contains zero or more objects to format.</param>
        public AkkaEqualException(object expected, object actual, string format = "", params object[] args)
            : base(expected, actual)
        {
            _format = format;
            _args = args;
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="AkkaEqualException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected AkkaEqualException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
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

                return $"{base.Message} {message}";
            }
        }
    }
}
