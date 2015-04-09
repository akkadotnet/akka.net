//-----------------------------------------------------------------------
// <copyright file="SinkCoordinator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;

namespace Akka.MultiNodeTestRunner.Shared.Sinks
{
    /// <summary>
    /// Top-level actor responsible for managing all <see cref="MessageSink"/> instances.
    /// </summary>
    public class SinkCoordinator : ReceiveActor
    {
        #region Message classes

        /// <summary>
        /// Used to signal that we need to enable a given <see cref="MessageSink"/> instance
        /// </summary>
        public class EnableSink
        {
            public EnableSink(MessageSink sink)
            {
                Sink = sink;
            }

            public MessageSink Sink { get; private set; }
        }

        /// <summary>
        /// Test run is complete. Shut down all sinks.
        /// 
        /// NOTE: Sending this message also means that the <see cref="ActorSystem"/> will be shut down.
        /// </summary>
        public class CloseAllSinks { }

        /// <summary>
        /// Confirms that a <see cref="MessageSink"/> has been closed
        /// </summary>
        public class SinkClosed { }

        /// <summary>
        /// Case class for distinguishing runner messages
        /// </summary>
        public class RunnerMessage
        {
            public RunnerMessage(string message)
            {
                Message = message;
            }

            public string Message { get; private set; }
        }

        /// <summary>
        /// Message that the <see cref="SinkCoordinator"/> will pass onto a <see cref="MessageSinkActor"/>
        /// </summary>
        public class RequestExitCode { }

        /// <summary>
        /// Response sent to <see cref="SinkCoordinator"/>
        /// </summary>
        public class RecommendedExitCode
        {
            public RecommendedExitCode(int code)
            {
                Code = code;
            }

            public int Code { get; private set; }
        }

        #endregion

        protected List<MessageSink> DefaultSinks;
        protected List<MessageSink> Sinks = new List<MessageSink>();

        protected int TotalReceiveClosedConfirmations = 0;
        protected int ReceivedSinkCloseConfirmations = 0;

        /// <summary>
        /// Leave the console message sink enabled by default
        /// </summary>
        public SinkCoordinator()
            : this(new[] { new ConsoleMessageSink() })
        {

        }

        public SinkCoordinator(IEnumerable<MessageSink> defaultSinks)
        {
            DefaultSinks = defaultSinks.ToList();
            InitializeReceives();
        }

        #region Actor lifecycle

        protected override void PreStart()
        {
            foreach(var sink in DefaultSinks)
                Self.Tell(new EnableSink(sink));
        }

        #endregion

        #region Message-handling

        private void InitializeReceives()
        {
            Receive<EnableSink>(sink =>
            {
                Sinks.Add(sink.Sink);
                sink.Sink.Open(Context.System);
            });

            Receive<SinkClosed>(closed =>
            {
                ReceivedSinkCloseConfirmations++;

                //Shut down the ActorSystem if all confirmations have been received
                if (ReceivedSinkCloseConfirmations >= TotalReceiveClosedConfirmations)
                    Context.System.Shutdown();
            });

            Receive<RecommendedExitCode>(code =>
            {
                ExitCodeContainer.ExitCode = code.Code;
            });

            Receive<CloseAllSinks>(sinks =>
            {
                //Ignore duplicate CloseAllSinks calls
                if (TotalReceiveClosedConfirmations > 0) return;

                TotalReceiveClosedConfirmations = Sinks.Count;
                ReceivedSinkCloseConfirmations = 0;

                foreach (var sink in Sinks)
                {
                    sink.RequestExitCode(Self);
                    sink.Close(Context.System)
                        .ContinueWith(r => new SinkClosed(),
                        TaskContinuationOptions.ExecuteSynchronously & TaskContinuationOptions.AttachedToParent)
                        .PipeTo(Self);
                }
            });
            Receive<string>(s =>
            {
                PublishToChildren(s);
            });
            Receive<NodeCompletedSpecWithSuccess>(s => PublishToChildren(s));
            Receive<IList<NodeTest>>(tests => BeginSpec(tests));
            Receive<EndSpec>(spec => EndSpec());
            Receive<RunnerMessage>(runner => PublishToChildren(runner));
        }

        private void PublishToChildren(NodeCompletedSpecWithSuccess message)
        {
            foreach(var sink in Sinks)
                sink.Success(message.NodeIndex, message.Message);
        }


        private void EndSpec()
        {
            foreach (var sink in Sinks)
                sink.EndTest();
        }

        private void BeginSpec(IList<NodeTest> tests)
        {
            var test = tests.First();

            foreach (var sink in Sinks)
                sink.BeginTest(test.TestName, test.MethodName, tests);
        }

        private void PublishToChildren(RunnerMessage message)
        {
            foreach (var sink in Sinks)
                sink.LogRunnerMessage(message.Message, Assembly.GetExecutingAssembly().GetName().Name, LogLevel.InfoLevel);
        }

        /// <summary>
        /// Publish a message to all <see cref="MessageSink"/> instances.
        /// </summary>
        private void PublishToChildren(string message)
        {
            foreach (var sink in Sinks)
                sink.Offer(message);
        }

        #endregion
    }
}

