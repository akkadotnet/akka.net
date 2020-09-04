//-----------------------------------------------------------------------
// <copyright file="ConfigurationException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
using Akka.Actor;

namespace Akka.Configuration
{
    /// <summary>
    /// The exception that is thrown when a configuration is invalid.
    /// </summary>
    public class ConfigurationException : AkkaException
    {
        public static ConfigurationException NullOrEmptyConfig<T>(string path = null)
        {
            if (!string.IsNullOrWhiteSpace(path))
                return new ConfigurationException($"Failed to instantiate {typeof(T).Name}: Configuration does not contain `{path}` node");
            return new ConfigurationException($"Failed to instantiate {typeof(T).Name}: Configuration is null or empty.");
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigurationException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public ConfigurationException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigurationException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="exception">The exception that is the cause of the current exception.</param>
        public ConfigurationException(string message, Exception exception): base(message, exception)
        {
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigurationException"/> class.
        /// </summary>
        /// <param name="info">The object that holds the serialized object data.</param>
        /// <param name="context">The contextual information about the source or destination.</param>
        protected ConfigurationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }
}

