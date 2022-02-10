//-----------------------------------------------------------------------
// <copyright file="AkkaSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

        public static Config AkkaSpecConfig { get { return _akkaSpecConfig; } }

        protected T ExpectMsgPf<T>(TimeSpan? timeout, string hint, Func<object, T> function)
            => ExpectMsgPf(timeout, hint, this, function);
        
        protected T ExpectMsgPf<T>(TimeSpan? timeout, string hint, TestKitBase probe, Func<object, T> function)
        {
            MessageEnvelope envelope;
            var success = probe.TryReceiveOne(out envelope, timeout);

            if(!success)
                Assertions.Fail(string.Format("expected message of type {0} but timed out after {1}", typeof(T), GetTimeoutOrDefault(timeout)));
            var message = envelope.Message;
            Assertions.AssertTrue(message != null, string.Format("expected {0} but got null message", hint));
            //TODO: Check next line. 
            Assertions.AssertTrue(function.GetMethodInfo().GetParameters().Any(x => x.ParameterType.IsInstanceOfType(message)), string.Format("expected {0} but got {1} instead", hint, message));
            return function.Invoke(message);
        }

        protected T ExpectMsgPf<T>(string hint, Func<object, T> pf)
            => ExpectMsgPf(hint, this, pf);

        protected T ExpectMsgPf<T>(string hint, TestKitBase probe, Func<object, T> pf)
        {
            var t = probe.ExpectMsg<T>();
            //TODO: Check if this really is needed:
            Assertions.AssertTrue(pf.GetMethodInfo().GetParameters().Any(x => x.ParameterType.IsInstanceOfType(t)), string.Format("expected {0} but got {1} instead", hint, t));
            return pf.Invoke(t);
        }

        /// <summary>
        /// Intercept and return an exception that's expected to be thrown by the passed function value. The thrown
        /// exception must be an instance of the type specified by the type parameter of this method. This method
        /// invokes the passed function. If the function throws an exception that's an instance of the specified type,
        /// this method returns that exception. Else, whether the passed function returns normally or completes abruptly
        /// with a different exception, this method throws <see cref="ThrowsException"/>.
        /// <para>
        /// Also note that the difference between this method and <seealso cref="AssertThrows{T}"/> is that this method
        /// returns the expected exception, so it lets you perform further assertions on that exception. By contrast,
        /// the <seealso cref="AssertThrows{T}"/> indicates to the reader of the code that nothing further is expected
        /// about the thrown exception other than its type. The recommended usage is to use <seealso cref="AssertThrows{T}"/>
        /// by default, intercept only when you need to inspect the caught exception further.
        /// </para>
        /// </summary>
        /// <param name="actionThatThrows">The action that should throw the expected exception</param>
        /// <returns>The intercepted exception, if it is of the expected type</returns>
        /// <exception cref="ThrowsException">If the passed action does not complete abruptly with an exception that's an instance of the specified type.</exception>
        protected T Intercept<T>(Action actionThatThrows) where T : Exception
        {
            try
            {
                actionThatThrows();
            }
            catch (Exception ex)
            {
                var exception = ex is AggregateException aggregateException
                    ? aggregateException.Flatten().InnerExceptions[0]
                    : ex;

                var exceptionType = typeof(T);
                return exceptionType == exception.GetType()
                    ? (T)exception
                    : throw new ThrowsException(exceptionType, exception);
            }

            throw new ThrowsException(typeof(T));
        }

        /// <summary>
        /// Ensure that an expected exception is thrown by the passed function value. The thrown exception must be an
        /// instance of the type specified by the type parameter of this method. This method invokes the passed
        /// function. If the function throws an exception that's an instance of the specified type, this method returns
        /// void. Else, whether the passed function returns normally or completes abruptly with a different
        /// exception, this method throws <see cref="ThrowsException"/>.
        /// <para>
        /// Also note that the difference between this method and <seealso cref="Intercept{T}"/> is that this method
        /// does not return the expected exception, so it does not let you perform further assertions on that exception.
        /// It also indicates to the reader of the code that nothing further is expected about the thrown exception
        /// other than its type. The recommended usage is to use <see cref="AssertThrows{T}"/> by default,
        /// <seealso cref="Intercept{T}"/> only when you need to inspect the caught exception further.
        /// </para>
        /// </summary>
        /// <param name="actionThatThrows">The action that should throw the expected exception</param>
        /// <exception cref="ThrowsException">If the passed action does not complete abruptly with an exception that's an instance of the specified type.</exception>
        protected void AssertThrows<T>(Action actionThatThrows) where T : Exception
        {
            try
            {
                actionThatThrows();
            }
            catch (Exception ex)
            {
                var exception = ex is AggregateException aggregateException
                    ? aggregateException.Flatten().InnerExceptions[0]
                    : ex;

                var exceptionType = typeof(T);
                if (exceptionType == exception.GetType())
                    return;

                throw new ThrowsException(exceptionType, exception);
            }

            throw new ThrowsException(typeof(T));
        }

        [Obsolete("Use AssertThrows instead.")]
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

