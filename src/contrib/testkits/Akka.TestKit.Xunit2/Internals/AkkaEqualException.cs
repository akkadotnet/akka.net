//-----------------------------------------------------------------------
// <copyright file="AkkaEqualException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
using Xunit.Sdk;

namespace Akka.TestKit.Xunit2.Internals
{
    public class AkkaEqualException : EqualException
    {
        private readonly string _format;
        private readonly object[] _args;

        public AkkaEqualException(object expected, object actual, string format = "", params object[] args)
            : base(expected, actual)
        {
            _format = format;
            _args = args;
        }
#if !DNXCORE50
        protected AkkaEqualException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif

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

