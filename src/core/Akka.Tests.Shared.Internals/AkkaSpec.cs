using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Util;
using Xunit;
using Xunit.Sdk;

// ReSharper disable once CheckNamespace
namespace Akka.TestKit
{
    public abstract class AkkaSpec : TestKit.Xunit.TestKit    //AkkaSpec is not part of TestKit
    {
        private static Regex _nameReplaceRegex = new Regex("[^a-zA-Z0-9]", RegexOptions.Compiled);
        private static readonly Config _akkaSpecConfig = ConfigurationFactory.ParseString(@"
          akka {
            loglevel = WARNING
            stdout-loglevel = WARNING
            serialize-messages = on
            actor {
              #default-dispatcher {
              #  executor = fork-join-executor
              #  fork-join-executor {
              #    parallelism-min = 8
              #    parallelism-factor = 2.0
              #    parallelism-max = 8
              #  }
              #}
            }
          }");

        private static int _systemNumber = 0;

        public AkkaSpec(string config)
            : this(ConfigurationFactory.ParseString(config).WithFallback(_akkaSpecConfig))
        {
        }

        public AkkaSpec(Config config = null)
            : base(config.SafeWithFallback(_akkaSpecConfig), GetCallerName())
        {
        }

        private static string GetCallerName()
        {
            var systemNumber = Interlocked.Increment(ref _systemNumber);
            var stackTrace = new StackTrace(0);
            var name = stackTrace.GetFrames().
                Select(f => f.GetMethod()).
                Where(m => m.DeclaringType != null).
                SkipWhile(m => m.DeclaringType.Name == "AkkaSpec").
                Select(m => _nameReplaceRegex.Replace(m.DeclaringType.Name + "-" + systemNumber, "-")).
                FirstOrDefault() ?? "test";
            return name;
        }

        protected static Config AkkaSpecConfig { get { return _akkaSpecConfig; } }

        protected void EventFilter<T>(int occurances, Action intercept) where T : Exception  //TODO: Replace when EventFilter class in akka-testkit\src\main\scala\akka\testkit\TestEventListener.scala has been implemented
        {
            EventFilter<T>(String.Empty, occurances, intercept);
        }

        protected void EventFilter<T>(string message, int occurances, Action intercept) where T : Exception  //TODO: Replace when EventFilter class in akka-testkit\src\main\scala\akka\testkit\TestEventListener.scala has been implemented
        {
            Sys.EventStream.Subscribe(TestActor, typeof(Error));
            intercept();
            for(int i = 0; i < occurances; i++)
            {
                var error = ExpectMsg<Error>();

                Assertions.AssertEqual(typeof(T), error.Cause.GetType());
                if(!string.IsNullOrEmpty(message)) //skip testing the message
                    Assertions.AssertEqual(message, error.Message);
            }
        }

        protected void EventFilterLog<T>(string message, int occurences, Action intercept) where T : LogEvent
        {
            Sys.EventStream.Subscribe(TestActor, typeof(T));
            intercept();
            for(int i = 0; i < occurences; i++)
            {
                var error = ExpectMsg<LogEvent>();

                Assertions.AssertEqual(typeof(T), error.GetType());
                var match = -1 != error.Message.ToString().IndexOf(message, StringComparison.CurrentCultureIgnoreCase);
                Assertions.AssertTrue(match);
            }
        }

        protected T ExpectMsgPf<T>(TimeSpan? timeout, string hint, Func<object, T> function)
        {
            MessageEnvelope envelope;
            var success = TryReceiveOne(out envelope, timeout);

            if(!success)
                Assertions.Fail(string.Format("expected message of type {0} but timed out after {1}", typeof(T), GetTimeoutOrDefault(timeout)));
            var message = envelope.Message;
            Assertions.AssertTrue(message != null, string.Format("expected {0} but got null message", hint));
            //TODO: Check next line. 
            Assertions.AssertTrue(function.Method.GetParameters().Any(x => x.ParameterType.IsInstanceOfType(message)), string.Format("expected {0} but got {1} instead", hint, message));
            return function.Invoke(message);
        }



        protected T ExpectMsgPf<T>(string hint, Func<object, T> pf)
        {
            var t = ExpectMsg<T>();
            //TODO: Check if this really is needed:
            Assertions.AssertTrue(pf.Method.GetParameters().Any(x => x.ParameterType.IsInstanceOfType(t)), string.Format("expected {0} but got {1} instead", hint, t));
            return pf.Invoke(t);
        }


        protected void Intercept<T>(Action actionThatThrows) where T : Exception
        {
            Assert.Throws<T>(() => actionThatThrows());
        }

        protected void Intercept(Action actionThatThrows)
        {
            try
            {
                actionThatThrows();                
            }
            catch(Exception)
            {
                return;
            }
            throw new ThrowsException(typeof(Exception));
        }

        [Obsolete("Use Intercept instead. This member will be removed.")]
        protected void intercept<T>(Action actionThatThrows) where T : Exception
        {
            Assert.Throws<T>(() => actionThatThrows());
        }

        [Obsolete("Use ExpectMsgPf instead. This member will be removed.")]
        protected T expectMsgPF<T>(string hint, Func<object, T> pf)
        {
            return ExpectMsgPf<T>(hint, pf);
        }

        [Obsolete("Use ExpectMsgPf instead. This member will be removed.")]
        protected T expectMsgPF<T>(TimeSpan duration, string hint, Func<object, T> pf)
        {
            return ExpectMsgPf<T>(duration, hint, pf);
        }

    }

    // ReSharper disable once InconsistentNaming
}
