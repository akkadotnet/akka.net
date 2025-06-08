//-----------------------------------------------------------------------
// <copyright file="TcpSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.IO
{
    /// <summary>
    /// Settings for Akka.IO.Tcp's outbound and inbound connection acvtors.
    /// </summary>
    public sealed record TcpSettings
    {
        /// <summary>
        /// Creates a new instance of <see cref="TcpSettings"/> class 
        /// and fills it with values parsed from `akka.io.tcp` HOCON
        /// path found in actor system.
        /// </summary>
        public static TcpSettings Create(ActorSystem system)
        {
            var config = system.Settings.Config.GetConfig("akka.io.tcp");
            if (config.IsNullOrEmpty())
                throw
                    ConfigurationException
                        .NullOrEmptyConfig<
                            TcpSettings>(
                            "akka.io.tcp"); 

            return Create(config);
        }

        /// <summary>
        /// Creates a new instance of <see cref="TcpSettings"/> class 
        /// and fills it with values parsed from provided HOCON config.
        /// </summary>
        /// <param name="config">The HOCON path that contains the `akka.io.tcp` section.</param>
        public static TcpSettings Create(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<TcpSettings>();

            return new TcpSettings(
                traceLogging: config.GetBoolean("trace-logging", false),
                batchAcceptLimit: config.GetString("batch-accept-limit") == "scale-to-cpus"
                    ? DefaultAcceptLimit
                    : config.GetInt("batch-accept-limit", DefaultAcceptLimit),
                registerTimeout: config.GetTimeSpan("register-timeout", TimeSpan.FromSeconds(5)),
                maxFrameSizeBytes: (int)config.GetByteSize("maximum-frame-size", 4096).Value,
                receiveBufferSize: (int)config.GetByteSize("receive-buffer-size", 8192).Value,
                sendBufferSize: (int)config.GetByteSize("send-buffer-size", 8192).Value,
                managementDispatcher: config.GetString("management-dispatcher", "akka.actor.default-dispatcher"),
                finishConnectRetries: config.GetInt("finish-connect-retries", 5),
                outgoingSocketForceIpv4: config.GetBoolean("outgoing-socket-force-ipv4", false),
                writeCommandsQueueMaxSize: config.GetInt("write-commands-queue-max-size", -1));
        }
        
        
        // private so we can change the constructor in the future
        private TcpSettings(
            bool traceLogging,
            int batchAcceptLimit,
            TimeSpan? registerTimeout,
            int maxFrameSizeBytes,
            int sendBufferSize,
            int receiveBufferSize,
            string managementDispatcher,
            int finishConnectRetries,
            bool outgoingSocketForceIpv4,
            int writeCommandsQueueMaxSize)
        {
            TraceLogging = traceLogging;
            BatchAcceptLimit = batchAcceptLimit;
            RegisterTimeout = registerTimeout;
            MaxFrameSizeBytes = maxFrameSizeBytes;
            SendBufferSize = sendBufferSize;
            ReceiveBufferSize = receiveBufferSize;
            
            // fail if send/receive buffer sizes are smaller than max frame size
            if (SendBufferSize < MaxFrameSizeBytes)
                throw new ArgumentException($"SendBufferSize ({SendBufferSize}) must be at least 2x the size of the maximum frame size ({MaxFrameSizeBytes})");
            if (ReceiveBufferSize < MaxFrameSizeBytes)
                throw new ArgumentException($"ReceiveBufferSize ({ReceiveBufferSize}) must be at least 2x the size of the maximum frame size ({MaxFrameSizeBytes})");
            
            // fail if the max frame size is negative
            if (MaxFrameSizeBytes < 0)
                throw new ArgumentException($"MaxFrameSizeBytes ({MaxFrameSizeBytes}) must be a positive number");
            
            FinishConnectRetries = finishConnectRetries;
            OutgoingSocketForceIpv4 = outgoingSocketForceIpv4;
            WriteCommandsQueueMaxSize = writeCommandsQueueMaxSize;
            ManagementDispatcher = managementDispatcher;
        }

        

        /// <summary>
        /// Default size of the SAEA pool
        /// </summary>
        internal static readonly int DefaultAcceptLimit = Environment.ProcessorCount * 2;

        [Obsolete("Many of these options are no longer used. Use the TcpSettings.Create method instead.")]
        public TcpSettings(string bufferPoolConfigPath,
            int initialSocketAsyncEventArgs,
            bool traceLogging,
            int batchAcceptLimit,
            TimeSpan? registerTimeout,
            int receivedMessageSizeLimit,
            string managementDispatcher,
            string fileIoDispatcher,
            int transferToLimit,
            int finishConnectRetries,
            bool outgoingSocketForceIpv4,
            int writeCommandsQueueMaxSize)
        {
            BufferPoolConfigPath = bufferPoolConfigPath;
            InitialSocketAsyncEventArgs = initialSocketAsyncEventArgs;
            TraceLogging = traceLogging;
            BatchAcceptLimit = batchAcceptLimit;
            RegisterTimeout = registerTimeout;
            MaxFrameSizeBytes = receivedMessageSizeLimit;
            
            // have to manually set these
            SendBufferSize = receivedMessageSizeLimit * 2;
            ReceiveBufferSize = receivedMessageSizeLimit * 2;
            
            ManagementDispatcher = managementDispatcher;
            FileIODispatcher = fileIoDispatcher;
            TransferToLimit = transferToLimit;
            FinishConnectRetries = finishConnectRetries;
            OutgoingSocketForceIpv4 = outgoingSocketForceIpv4;
            WriteCommandsQueueMaxSize = writeCommandsQueueMaxSize;
        }

        /// <summary>
        /// A config path to the section defining which byte buffer pool to use.
        /// Buffer pools are used to mitigate GC-pressure made by potential allocation
        /// and deallocation of byte buffers used for writing/receiving data from sockets.
        /// </summary>
        [Obsolete("This property is unused")]
        public string BufferPoolConfigPath { get; }

        /// <summary>
        /// The initial number of SocketAsyncEventArgs to be preallocated. This value
        /// will grow infinitely if needed.
        /// </summary>
        [Obsolete("This property is unused")]
        public int InitialSocketAsyncEventArgs { get; }

        /// <summary>
        /// Enable fine grained logging of what goes on inside the implementation. 
        /// Be aware that this may log more than once per message sent to the 
        /// actors of the tcp implementation.
        /// </summary>
        public bool TraceLogging { get; init; }

        /// <summary>
        /// The maximum number of connection that are accepted in one go, higher 
        /// numbers decrease latency, lower numbers increase fairness on the 
        /// worker-dispatcher
        /// </summary>
        public int BatchAcceptLimit { get; init; }

        /// <summary>
        /// The duration a connection actor waits for a `Register` message from 
        /// its commander before aborting the connection.
        /// </summary>
        public TimeSpan? RegisterTimeout { get; init; }
        
        /// <summary>
        /// The maximum frame size we will accept when reading or writing to a socket.
        /// </summary>
        
        public int MaxFrameSizeBytes { get; init; }
        
        /// <summary>
        /// Should be at least 2x the size of the maximum frame size.
        /// </summary>
        public int ReceiveBufferSize { get; init; }
        
        /// <summary>
        /// Should be at least 2x the size of the maximum frame size.
        /// </summary>
        public int SendBufferSize { get; init; }

        /// <summary>
        /// The maximum number of bytes delivered by a `Received` message. Before
        /// more data is read from the network the connection actor will try to
        /// do other work.
        /// The purpose of this setting is to impose a smaller limit than the 
        /// configured receive buffer size. When using value 'unlimited' it will
        /// try to read all from the receive buffer.
        /// </summary>
        [Obsolete("This property is now MaxFrameSizeBytes")]
        public long ReceivedMessageSizeLimit => MaxFrameSizeBytes;

        /// <summary>
        /// Fully qualified config path which holds the dispatcher configuration
        /// for the selector management actors
        /// </summary>
        public string ManagementDispatcher { get; }

        /// <summary>
        /// Fully qualified config path which holds the dispatcher configuration
        /// on which file IO tasks are scheduled
        /// </summary>
        [Obsolete("This property is unused")]
        public string FileIODispatcher { get; }

        /// <summary>
        /// The maximum number of bytes (or "unlimited") to transfer in one batch
        /// when using `WriteFile` command which uses `FileChannel.transferTo` to
        /// pipe files to a TCP socket. On some OS like Linux `FileChannel.transferTo`
        /// may block for a long time when network IO is faster than file IO.
        /// Decreasing the value may improve fairness while increasing may improve
        /// throughput.
        /// </summary>
        [Obsolete("This property is unused")]
        public int TransferToLimit { get; set; }

        /// <summary>
        /// The number of times to retry the `finishConnect` call after being notified about
        /// OP_CONNECT. Retries are needed if the OP_CONNECT notification doesn't imply that
        /// `finishConnect` will succeed, which is the case on Android.
        /// </summary>
        public int FinishConnectRetries { get; init; }

        /// <summary>
        /// Enforce outgoing socket connection to use IPv4 address family. Required in
        /// a scenario when IPv6 is not available, for example in Azure Web App sandbox.
        /// When set to true it is required to set akka.io.dns.inet-address.use-ipv6 to false
        /// in cases when DnsEndPoint is used to describe the remote address
        /// </summary>
        public bool OutgoingSocketForceIpv4 { get; init; }

        /// <summary>
        /// Limits maximum size of internal queue, used in <see cref="TcpIncomingConnection"/> connection actor
        /// to store pending write commands.
        /// To allow unlimited size, set to -1.
        /// </summary>
        /// <remarks>
        /// This setting defines the maximum number of messages, not the maximum size in bytes.
        /// </remarks>
        public int WriteCommandsQueueMaxSize { get; init; }
    }
}