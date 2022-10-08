﻿//-----------------------------------------------------------------------
// <copyright file="UdpSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.IO
{
    public class UdpSettings
    {
        /// <summary>
        /// Creates a new instance of <see cref="UdpSettings"/> class 
        /// and fills it with values parsed from `akka.io.udp` HOCON
        /// path found in actor system.
        /// </summary>
        public static UdpSettings Create(ActorSystem system)
        {
            var config = system.Settings.Config.GetConfig("akka.io.udp");
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<UdpSettings>("akka.io.udp");

            return Create(config);
        }

        /// <summary>
        /// Creates a new instance of <see cref="UdpSettings"/> class 
        /// and fills it with values parsed from provided HOCON config.
        /// </summary>
        /// <param name="config">TBD</param>
        public static UdpSettings Create(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<UdpSettings>();

            return new UdpSettings(
                bufferPoolConfigPath: config.GetString("buffer-pool", null),
                traceLogging: config.GetBoolean("trace-logging", false),
                initialSocketAsyncEventArgs: config.GetInt("nr-of-socket-async-event-args", 32),
                batchReceiveLimit: config.GetInt("receive-throughput", 0),
                managementDispatcher: config.GetString("management-dispatcher", "akka.actor.default-dispatcher"),
                fileIoDispatcher: config.GetString("file-io-dispatcher", "akka.actor.default-dispatcher"));
        }
        
        // directBufferSize and maxDirectBufferPoolSize parameter are byterot, remove this in v1.5
        [Obsolete("Please the constructor without directBufferSize and maxDirectBufferPoolSize parameter")]
        public UdpSettings(
            string bufferPoolConfigPath, 
            bool traceLogging,
            int initialSocketAsyncEventArgs,
            int directBufferSize,
            int maxDirectBufferPoolSize,
            int batchReceiveLimit,
            string managementDispatcher,
            string fileIoDispatcher) 
            : this(
                bufferPoolConfigPath,
                traceLogging,
                initialSocketAsyncEventArgs,
                batchReceiveLimit,
                managementDispatcher,
                fileIoDispatcher)
        {
        }

        public UdpSettings(string bufferPoolConfigPath, bool traceLogging, int initialSocketAsyncEventArgs, int batchReceiveLimit, string managementDispatcher, string fileIoDispatcher)
        {
            BufferPoolConfigPath = bufferPoolConfigPath;
            TraceLogging = traceLogging;
            InitialSocketAsyncEventArgs = initialSocketAsyncEventArgs;
            BatchReceiveLimit = batchReceiveLimit;
            ManagementDispatcher = managementDispatcher;
            FileIODispatcher = fileIoDispatcher;
        }

        /// <summary>
        /// A config path to the section defining which byte buffer pool to use.
        /// Buffer pools are used to mitigate GC-pressure made by potentiall allocation
        /// and deallocation of byte buffers used for writing/receiving data from sockets.
        /// </summary>
        public string BufferPoolConfigPath { get; }

        /// <summary>
        /// Enable fine grained logging of what goes on inside the implementation. 
        /// Be aware that this may log more than once per message sent to the 
        /// actors of the tcp implementation.
        /// </summary>
        public bool TraceLogging { get; }

        /// <summary>
        /// The initial number of SocketAsyncEventArgs to be preallocated. This value
        /// will grow infinitely if needed.
        /// </summary>
        public int InitialSocketAsyncEventArgs { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public int BatchReceiveLimit { get; }

        /// <summary>
        /// Fully qualified config path which holds the dispatcher configuration
        /// for the selector management actors
        /// </summary>
        public string ManagementDispatcher { get; }

        /// <summary>
        /// Fully qualified config path which holds the dispatcher configuration
        /// on which file IO tasks are scheduled
        /// </summary>
        public string FileIODispatcher { get; }
    }
}
