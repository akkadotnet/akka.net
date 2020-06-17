//-----------------------------------------------------------------------
// <copyright file="AkkaSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.TestKit.Internal.StringMatcher;
using Akka.TestKit.TestEvent;
using Akka.Util;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

// ReSharper disable once CheckNamespace
namespace Akka.TestKit
{
    public abstract class AkkaSpec : Xunit2.TestKit    //AkkaSpec is not part of TestKit
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
          }
          # use random ports to avoid race conditions with binding contention
          akka.remote.dot-netty.tcp.port = 0");

        private static int _systemNumber = 0;

        public AkkaSpec(string config, ITestOutputHelper output = null)
            : this(ConfigurationFactory.ParseString(config).WithFallback(_akkaSpecConfig), output)
        {
        }

        public AkkaSpec(Config config = null, ITestOutputHelper output = null)
            : base(config.SafeWithFallback(_akkaSpecConfig), GetCallerName(), output)
        {
            BeforeAll();
        }

        public AkkaSpec(ActorSystemSetup setup, ITestOutputHelper output = null)
            : base(setup, GetCallerName(), output)
        {
            BeforeAll();
        }

        public AkkaSpec(ITestOutputHelper output, Config config = null)
            : base(config.SafeWithFallback(_akkaSpecConfig), GetCallerName(), output)
        {
            BeforeAll();
        }

        public AkkaSpec(ActorSystem system, ITestOutputHelper output = null)
            : base(system, output)
        {
            BeforeAll();
        }

        private void BeforeAll()
        {
            GC.Collect();
            AtStartup();
        }

        protected override void AfterAll()
        {
            BeforeTermination();
            base.AfterAll();
            AfterTermination();
        }

        protected virtual void AtStartup() { }

        protected virtual void BeforeTermination() { }

        protected virtual void AfterTermination() { }

        private static string GetCallerName()
        {
#if CORECLR
            // TODO: CORECLR FIX IT
            var name = "AkkaSpec";
#else
            var systemNumber = Interlocked.Increment(ref _systemNumber);
            var stackTrace = new StackTrace(0);
            var name = stackTrace.GetFrames().
                Select(f => f.GetMethod()).
                Where(m => m.DeclaringType != null).
                SkipWhile(m => m.DeclaringType.Name == "AkkaSpec").
                Select(m => _nameReplaceRegex.Replace(m.DeclaringType.Name + "-" + systemNumber, "-")).
                FirstOrDefault() ?? "test";
#endif
            return name;
        }

        public static Config AkkaSpecConfig { get { return _akkaSpecConfig; } }
        
        protected T ExpectMsgPf<T>(TimeSpan? timeout, string hint, Func<object, T> function)
        {
            MessageEnvelope envelope;
            var success = TryReceiveOne(out envelope, timeout);

            if(!success)
                Assertions.Fail(string.Format("expected message of type {0} but timed out after {1}", typeof(T), GetTimeoutOrDefault(timeout)));
            var message = envelope.Message;
            Assertions.AssertTrue(message != null, string.Format("expected {0} but got null message", hint));
            //TODO: Check next line. 
            Assertions.AssertTrue(function.GetMethodInfo().GetParameters().Any(x => x.ParameterType.IsInstanceOfType(message)), string.Format("expected {0} but got {1} instead", hint, message));
            return function.Invoke(message);
        }

        protected T ExpectMsgPf<T>(string hint, Func<object, T> pf)
        {
            var t = ExpectMsg<T>();
            //TODO: Check if this really is needed:
            Assertions.AssertTrue(pf.GetMethodInfo().GetParameters().Any(x => x.ParameterType.IsInstanceOfType(t)), string.Format("expected {0} but got {1} instead", hint, t));
            return pf.Invoke(t);
        }


        protected T Intercept<T>(Action actionThatThrows) where T : Exception
        {
            return Assert.Throws<T>(() => actionThatThrows());
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
        
        protected async Task InterceptAsync(Func<Task> asyncActionThatThrows)
        {
            try
            {
                await asyncActionThatThrows();
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

        protected void MuteDeadLetters(params Type[] messageClasses)
        {
            if (!Sys.Log.IsDebugEnabled)
                return;

            Action<Type> mute =
                clazz =>
                    Sys.EventStream.Publish(
                        new Mute(new DeadLettersFilter(new PredicateMatcher(_ => true),
                            new PredicateMatcher(_ => true),
                            letter => clazz == typeof(object) || letter.Message.GetType() == clazz)));

            if (messageClasses.Length == 0)
                mute(typeof(object));
            else
                messageClasses.ForEach(mute);
        }
    }

    // ReSharper disable once InconsistentNaming
}

