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
              ask-timeout = 20s
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

        protected override async Task AfterAllAsync()
        {
            await BeforeTerminationAsync();
            await base.AfterAllAsync();
            await AfterTerminationAsync();
        }

        protected virtual void AtStartup() { }

        protected virtual Task BeforeTerminationAsync()
        {
            return Task.CompletedTask;
        }

        protected virtual Task AfterTerminationAsync()
        {
            return Task.CompletedTask;
        }

        private static string GetCallerName()
        {
            var systemNumber = Interlocked.Increment(ref _systemNumber);
            var stackTrace = new StackTrace(0);
            var name = stackTrace.GetFrames()?
                .Select(f => f.GetMethod())
                .Where(m => m.DeclaringType != null)
                .SkipWhile(m => m.DeclaringType.Name == "AkkaSpec")
                .Select(m => _nameReplaceRegex.Replace(m.DeclaringType.Name + "-" + systemNumber, "-"))
                .FirstOrDefault() ?? "test";

            return name;
        }

        public static Config AkkaSpecConfig { get { return _akkaSpecConfig; } }

        protected T ExpectMsgOf<T>(
            TimeSpan? timeout,
            string hint,
            Func<object, T> function,
            CancellationToken cancellationToken = default)
            => ExpectMsgOfAsync(timeout, hint, this, function, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();

        protected async Task<T> ExpectMsgOfAsync<T>(
            TimeSpan? timeout,
            string hint,
            Func<object, T> function,
            CancellationToken cancellationToken = default)
            => await ExpectMsgOfAsync(timeout, hint, this, function, cancellationToken)
                .ConfigureAwait(false);

        protected async Task<T> ExpectMsgOfAsync<T>(
            TimeSpan? timeout,
            string hint,
            Func<object, Task<T>> function,
            CancellationToken cancellationToken = default)
            => await ExpectMsgOfAsync(timeout, hint, this, function, cancellationToken)
                .ConfigureAwait(false);
        
        protected T ExpectMsgOf<T>(
            TimeSpan? timeout,
            string hint,
            TestKitBase probe,
            Func<object, T> function,
            CancellationToken cancellationToken = default)
            => ExpectMsgOfAsync(timeout, hint, probe, function, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
        
        protected async Task<T> ExpectMsgOfAsync<T>(
            TimeSpan? timeout,
            string hint,
            TestKitBase probe,
            Func<object, T> function,
            CancellationToken cancellationToken = default)
            => await ExpectMsgOfAsync(timeout, hint, probe, o => Task.FromResult(function(o)), cancellationToken)
                .ConfigureAwait(false);
        
        protected async Task<T> ExpectMsgOfAsync<T>(
            TimeSpan? timeout,
            string hint,
            TestKitBase probe,
            Func<object, Task<T>> function,
            CancellationToken cancellationToken = default)
        {
            var (success, envelope) = await probe.TryReceiveOneAsync(timeout, cancellationToken)
                .ConfigureAwait(false);

            if(!success)
                Assertions.Fail($"expected message of type {typeof(T)} but timed out after {GetTimeoutOrDefault(timeout)}");
            
            var message = envelope.Message;
            Assertions.AssertTrue(message != null, $"expected {hint} but got null message");
            //TODO: Check next line. 
            Assertions.AssertTrue(
                function.GetMethodInfo().GetParameters().Any(x => x.ParameterType.IsInstanceOfType(message)),
                $"expected {hint} but got {message} instead");
            
            return await function(message).ConfigureAwait(false);
        }

        protected T ExpectMsgOf<T>(
            string hint,
            TestKitBase probe,
            Func<object, T> pf,
            CancellationToken cancellationToken = default)
            => ExpectMsgOfAsync(hint, probe, pf, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();

        protected async Task<T> ExpectMsgOfAsync<T>(
            string hint,
            TestKitBase probe,
            Func<object, T> pf,
            CancellationToken cancellationToken = default)
            => await ExpectMsgOfAsync(hint, probe, o => Task.FromResult(pf(o)), cancellationToken)
                .ConfigureAwait(false);
        
        protected async Task<T> ExpectMsgOfAsync<T>(
            string hint,
            TestKitBase probe,
            Func<object, Task<T>> pf,
            CancellationToken cancellationToken = default)
        {
            var t = await probe.ExpectMsgAsync<T>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            
            //TODO: Check if this really is needed:
            Assertions.AssertTrue(pf.GetMethodInfo().GetParameters().Any(x => x.ParameterType.IsInstanceOfType(t)),
                $"expected {hint} but got {t} instead");
            return await pf(t);
        }

        protected T ExpectMsgOf<T>(
            string hint,
            Func<object, T> pf,
            CancellationToken cancellationToken = default)
            => ExpectMsgOfAsync(hint, this, pf, cancellationToken)
                .ConfigureAwait(false).GetAwaiter().GetResult();
        
        protected async Task<T> ExpectMsgOfAsync<T>(
            string hint,
            Func<object, T> pf,
            CancellationToken cancellationToken = default)
            => await ExpectMsgOfAsync(hint, this, pf, cancellationToken)
                .ConfigureAwait(false);
        
        protected async Task<T> ExpectMsgOfAsync<T>(
            string hint,
            Func<object, Task<T>> pf,
            CancellationToken cancellationToken = default)
            => await ExpectMsgOfAsync(hint, this, pf, cancellationToken)
                .ConfigureAwait(false);
        
        [Obsolete("Method name typo, please use ExpectMsgOf instead")]
        protected T ExpectMsgPf<T>(TimeSpan? timeout, string hint, Func<object, T> function)
            => ExpectMsgOf(timeout, hint, this, function);
        
        [Obsolete("Method name typo, please use ExpectMsgOf instead")]
        protected T ExpectMsgPf<T>(TimeSpan? timeout, string hint, TestKitBase probe, Func<object, T> function)
            => ExpectMsgOf(timeout, hint, probe, function);

        [Obsolete("Method name typo, please use ExpectMsgOf instead")]
        protected T ExpectMsgPf<T>(string hint, Func<object, T> pf)
            => ExpectMsgOf(hint, this, pf);

        [Obsolete("Method name typo, please use ExpectMsgOf instead")]
        protected T ExpectMsgPf<T>(string hint, TestKitBase probe, Func<object, T> pf)
            => ExpectMsgOf(hint, probe, pf);

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
            => MuteDeadLetters(Sys, messageClasses);

        protected void MuteDeadLetters(ActorSystem sys, params Type[] messageClasses)
        {
            if (!sys.Log.IsDebugEnabled)
                return;

            Action<Type> mute =
                clazz =>
                    sys.EventStream.Publish(
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

