//-----------------------------------------------------------------------
// <copyright file="AkkaEqualException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    public class AkkaEqualException : EqualException
    {
        private readonly string _format;
        private readonly object[] _args;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="expected">TBD</param>
        /// <param name="actual">TBD</param>
        /// <param name="format">TBD</param>
        /// <param name="args">TBD</param>
        public AkkaEqualException(object expected, object actual, string format = "", params object[] args)
            : base(expected, actual)
        {
            _format = format;
            _args = args;
        }

#if SERIALIZATION
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="info">TBD</param>
        /// <param name="context">TBD</param>
        protected AkkaEqualException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
        /// <summary>
        /// TBD
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
                    message = "[Could not string.Format(\"" + _format + "\", " + string.Join(", ", _args) + ")]";
                }
                return base.Message + " " + message;
            }
        }
    }
}