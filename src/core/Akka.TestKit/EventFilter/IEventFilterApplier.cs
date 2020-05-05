//-----------------------------------------------------------------------
// <copyright file="IEventFilterApplier.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Akka.TestKit
{
    // ReSharper disable once InconsistentNaming
    /// <summary>
    /// TBD
    /// </summary>
    public interface IEventFilterApplier
    {
        /// <summary>
        /// Executes <paramref name="action"/> and
        /// expects one event to be logged during the execution.
        /// This method fails and throws an exception if more than one event is logged,
        /// or if a timeout occurs. The timeout is taken from the config value
        /// "akka.test.filter-leeway", see <see cref="TestKitSettings.TestEventFilterLeeway"/>.
        /// </summary>
        /// <param name="action">The action.</param>
        void ExpectOne(Action action);
        
        /// <summary>
        /// Executes <paramref name="action"/> and
        /// expects one event to be logged during the execution.
        /// This method fails and throws an exception if more than one event is logged,
        /// or if a timeout occurs. The timeout is taken from the config value
        /// "akka.test.filter-leeway", see <see cref="TestKitSettings.TestEventFilterLeeway"/>.
        /// </summary>
        /// <param name="action">The action.</param>
        Task ExpectOneAsync(Action action);
        
        /// <summary>
        /// Executes <paramref name="actionAsync"/> and
        /// expects one event to be logged during the execution.
        /// This method fails and throws an exception if more than one event is logged,
        /// or if a timeout occurs. The timeout is taken from the config value
        /// "akka.test.filter-leeway", see <see cref="TestKitSettings.TestEventFilterLeeway"/>.
        /// </summary>
        /// <param name="actionAsync">The action.</param>
        Task ExpectOneAsync(Func<Task> actionAsync);

        /// <summary>
        /// Executes <paramref name="action"/> and
        /// expects one event to be logged during the execution.
        /// This method fails and throws an exception if more than one event is logged,
        /// or if a timeout occurs.
        /// </summary>
        /// <param name="timeout">The time to wait for a log event after executing <paramref name="action"/></param>
        /// <param name="action">The action.</param>
        void ExpectOne(TimeSpan timeout, Action action);
        
        /// <summary>
        /// Executes <paramref name="action"/> and
        /// expects one event to be logged during the execution.
        /// This method fails and throws an exception if more than one event is logged,
        /// or if a timeout occurs.
        /// </summary>
        /// <param name="timeout">The time to wait for a log event after executing <paramref name="action"/></param>
        /// <param name="action">The action.</param>
        Task ExpectOneAsync(TimeSpan timeout, Action action);
        
        /// <summary>
        /// Executes <paramref name="action"/> and expects the specified number
        /// of events to be logged during the execution.
        /// This method fails and throws an exception if more events than expected are logged,
        /// or if a timeout occurs. The timeout is taken from the config value
        /// "akka.test.filter-leeway", see <see cref="TestKitSettings.TestEventFilterLeeway"/>.
        /// </summary>
        /// <param name="expectedCount">The expected number of events</param>
        /// <param name="action">The action.</param>
        void Expect(int expectedCount, Action action);
        
        /// <summary>
        /// Executes <paramref name="action"/> and expects the specified number
        /// of events to be logged during the execution.
        /// This method fails and throws an exception if more events than expected are logged,
        /// or if a timeout occurs. The timeout is taken from the config value
        /// "akka.test.filter-leeway", see <see cref="TestKitSettings.TestEventFilterLeeway"/>.
        /// </summary>
        /// <param name="expectedCount">The expected number of events</param>
        /// <param name="action">The action.</param>
        Task ExpectAsync(int expectedCount, Action action);
        
        /// <summary>
        /// Executes <paramref name="actionAsync"/> task and expects the specified number
        /// of events to be logged during the execution.
        /// This method fails and throws an exception if more events than expected are logged,
        /// or if a timeout occurs. The timeout is taken from the config value
        /// "akka.test.filter-leeway", see <see cref="TestKitSettings.TestEventFilterLeeway"/>.
        /// </summary>
        /// <param name="expectedCount">The expected number of events</param>
        /// <param name="actionAsync">The async action.</param>
        Task ExpectAsync(int expectedCount, Func<Task> actionAsync);

        /// <summary>
        /// Executes <paramref name="action"/> and expects the specified number
        /// of events to be logged during the execution.
        /// This method fails and throws an exception if more events than expected are logged,
        /// or if a timeout occurs. The timeout is taken from the config value
        /// "akka.test.filter-leeway", see <see cref="TestKitSettings.TestEventFilterLeeway"/>.
        /// </summary>
        /// <param name="timeout">The time to wait for log events after executing <paramref name="action"/></param>
        /// <param name="expectedCount">The expected number of events</param>
        /// <param name="action">The action.</param>
        void Expect(int expectedCount, TimeSpan timeout, Action action);
        
        /// <summary>
        /// Executes <paramref name="action"/> and expects the specified number
        /// of events to be logged during the execution.
        /// This method fails and throws an exception if more events than expected are logged,
        /// or if a timeout occurs. The timeout is taken from the config value
        /// "akka.test.filter-leeway", see <see cref="TestKitSettings.TestEventFilterLeeway"/>.
        /// </summary>
        /// <param name="timeout">The time to wait for log events after executing <paramref name="action"/></param>
        /// <param name="expectedCount">The expected number of events</param>
        /// <param name="action">The action.</param>
        Task ExpectAsync(int expectedCount, TimeSpan timeout, Action action);

        /// <summary>
        /// Executes <paramref name="func"/> and
        /// expects one event to be logged during the execution.
        /// This function fails and throws an exception if more than one event is logged,
        /// or if a timeout occurs. The timeout is taken from the config value
        /// "akka.test.filter-leeway", see <see cref="TestKitSettings.TestEventFilterLeeway"/>.
        /// </summary>
        /// <typeparam name="T">The return value of the function</typeparam>
        /// <param name="func">The function.</param>
        /// <returns>The returned value from <paramref name="func"/>.</returns>
        T ExpectOne<T>(Func<T> func);
        
        /// <summary>
        /// Executes <paramref name="func"/> and
        /// expects one event to be logged during the execution.
        /// This function fails and throws an exception if more than one event is logged,
        /// or if a timeout occurs. The timeout is taken from the config value
        /// "akka.test.filter-leeway", see <see cref="TestKitSettings.TestEventFilterLeeway"/>.
        /// </summary>
        /// <typeparam name="T">The return value of the function</typeparam>
        /// <param name="func">The function.</param>
        /// <returns>The returned value from <paramref name="func"/>.</returns>
        Task<T> ExpectOneAsync<T>(Func<T> func);

        /// <summary>
        /// Executes <paramref name="func"/> and
        /// expects one event to be logged during the execution.
        /// This function fails and throws an exception if more than one event is logged,
        /// or if a timeout occurs.
        /// </summary>
        /// <typeparam name="T">The return value of the function</typeparam>
        /// <param name="timeout">The time to wait for a log event after executing <paramref name="func"/></param>
        /// <param name="func">The function.</param>
        /// <returns>The returned value from <paramref name="func"/>.</returns>
        T ExpectOne<T>(TimeSpan timeout, Func<T> func);
        
        /// <summary>
        /// Executes <paramref name="func"/> and
        /// expects one event to be logged during the execution.
        /// This function fails and throws an exception if more than one event is logged,
        /// or if a timeout occurs.
        /// </summary>
        /// <typeparam name="T">The return value of the function</typeparam>
        /// <param name="timeout">The time to wait for a log event after executing <paramref name="func"/></param>
        /// <param name="func">The function.</param>
        /// <returns>The returned value from <paramref name="func"/>.</returns>
        Task<T> ExpectOneAsync<T>(TimeSpan timeout, Func<T> func);

        /// <summary>
        /// Executes <paramref name="func"/> and expects the specified number
        /// of events to be logged during the execution.
        /// This function fails and throws an exception if more events than expected are logged,
        /// or if a timeout occurs. The timeout is taken from the config value
        /// "akka.test.filter-leeway", see <see cref="TestKitSettings.TestEventFilterLeeway"/>.
        /// </summary>
        /// <typeparam name="T">The return value of the function</typeparam>
        /// <param name="expectedCount">The expected number of events</param>
        /// <param name="func">The function.</param>
        /// <returns>The returned value from <paramref name="func"/>.</returns>
        T Expect<T>(int expectedCount, Func<T> func);
        
        /// <summary>
        /// Executes <paramref name="func"/> and expects the specified number
        /// of events to be logged during the execution.
        /// This function fails and throws an exception if more events than expected are logged,
        /// or if a timeout occurs. The timeout is taken from the config value
        /// "akka.test.filter-leeway", see <see cref="TestKitSettings.TestEventFilterLeeway"/>.
        /// </summary>
        /// <typeparam name="T">The return value of the function</typeparam>
        /// <param name="expectedCount">The expected number of events</param>
        /// <param name="func">The function.</param>
        /// <returns>The returned value from <paramref name="func"/>.</returns>
        Task<T> ExpectAsync<T>(int expectedCount, Func<T> func);

        /// <summary>
        /// Executes <paramref name="func"/> and expects the specified number
        /// of events to be logged during the execution.
        /// This function fails and throws an exception if more events than expected are logged,
        /// or if a timeout occurs. The timeout is taken from the config value
        /// "akka.test.filter-leeway", see <see cref="TestKitSettings.TestEventFilterLeeway"/>.
        /// </summary>
        /// <typeparam name="T">The return value of the function</typeparam>
        /// <param name="timeout">The time to wait for log events after executing <paramref name="func"/></param>
        /// <param name="expectedCount">The expected number of events</param>
        /// <param name="func">The function.</param>
        /// <returns>The returned value from <paramref name="func"/>.</returns>
        T Expect<T>(int expectedCount, TimeSpan timeout, Func<T> func);
        
        /// <summary>
        /// Executes <paramref name="func"/> and expects the specified number
        /// of events to be logged during the execution.
        /// This function fails and throws an exception if more events than expected are logged,
        /// or if a timeout occurs. The timeout is taken from the config value
        /// "akka.test.filter-leeway", see <see cref="TestKitSettings.TestEventFilterLeeway"/>.
        /// </summary>
        /// <typeparam name="T">The return value of the function</typeparam>
        /// <param name="timeout">The time to wait for log events after executing <paramref name="func"/></param>
        /// <param name="expectedCount">The expected number of events</param>
        /// <param name="func">The function.</param>
        /// <returns>The returned value from <paramref name="func"/>.</returns>
        Task<T> ExpectAsync<T>(int expectedCount, TimeSpan timeout, Func<T> func);

        /// <summary>
        /// Executes <paramref name="func"/> and prevent events from being logged during the execution.
        /// </summary>
        /// <typeparam name="T">The return value of the function</typeparam>
        /// <param name="func">The function.</param>
        /// <returns>The returned value from <paramref name="func"/>.</returns>
        T Mute<T>(Func<T> func);
        
        /// <summary>
        /// Executes <paramref name="func"/> and prevent events from being logged during the execution.
        /// </summary>
        /// <typeparam name="T">The return value of the function</typeparam>
        /// <param name="func">The function.</param>
        /// <returns>The returned value from <paramref name="func"/>.</returns>
        Task<T> MuteAsync<T>(Func<T> func);

        /// <summary>
        /// Executes <paramref name="action"/> and prevent events from being logged during the execution.
        /// </summary>
        /// <param name="action">The function.</param>
        /// <returns>The returned value from <paramref name="action"/>.</returns>
        void Mute(Action action);
        
        /// <summary>
        /// Executes <paramref name="action"/> and prevent events from being logged during the execution.
        /// </summary>
        /// <param name="action">The function.</param>
        /// <returns>The returned value from <paramref name="action"/>.</returns>
        Task MuteAsync(Action action);

        /// <summary>
        /// Prevents events from being logged from now on. To allow events to be logged again, call 
        /// <see cref="IUnmutableFilter.Unmute"/> on the returned object.
        /// <example>
        /// <code>
        /// var filter = EventFilter.Debug().Mute();
        /// ...
        /// filter.Unmute();
        /// </code>
        /// </example>
        /// You may also use it like this:
        /// <example>
        /// <code>
        /// using(EventFilter.Debug().Mute())
        /// {
        ///    ...
        /// }
        /// </code>
        /// </example>
        /// </summary>
        /// <returns>TBD</returns>
        IUnmutableFilter Mute();

        /// <summary>
        /// Let's you chain more filters together. Similar to Akka JVM's filterEvents
        /// </summary>
        EventFilterFactory And { get; }
    }
}
