//-----------------------------------------------------------------------
// <copyright file="PatternMatch.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka
{
    /// <summary>
    /// Class PatternMatch.
    /// </summary>
    public static class PatternMatch
    {
        
        /// <summary>
        /// Matches the specified target.
        /// </summary>
        /// <param name="target">The target.</param>
        /// <returns>Case.</returns>
        public static Case Match(this object target)
        {
            return new Case(target);
        }

        /// <summary>
        /// Matches the specified target and return a result of target processing.
        /// </summary>
        /// <param name="target">The target.</param>
        /// <returns>Case.</returns>
        public static Case<T> Match<T>(this object target)
        {
            return new Case<T>(target);
        }
    }

    /// <summary>
    /// Interface IMatchResult
    /// </summary>
    public interface IMatchResult
    {
        /// <summary>
        /// Gets a value indicating whether [was handled].
        /// </summary>
        /// <value><c>true</c> if [was handled]; otherwise, <c>false</c>.</value>
        bool WasHandled { get; }
    }

    /// <summary>
    /// Class Case.
    /// </summary>
    public class Case : IMatchResult
    {
        /// <summary>
        /// The _message
        /// </summary>
        private readonly object _message;
        /// <summary>
        /// The _handled
        /// </summary>
        private bool _handled;
        /// <summary>
        /// Gets a value indicating whether [was handled].
        /// </summary>
        /// <value><c>true</c> if [was handled]; otherwise, <c>false</c>.</value>
        public bool WasHandled { get { return _handled; } }

        /// <summary>
        /// Initializes a new instance of the <see cref="Case"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        public Case(object message)
        {
            _message = message;
        }

        /// <summary>
        /// Withes the specified action.
        /// </summary>
        /// <typeparam name="TMessage">The type of the t message.</typeparam>
        /// <param name="action">The action.</param>
        /// <returns>Case.</returns>
        public Case With<TMessage>(Action action)
        {
            if (!_handled && _message is TMessage)
            {
                action();
                _handled = true;
            }

            return this;
        }

        /// <summary>
        /// Withes the specified action.
        /// </summary>
        /// <typeparam name="TMessage">The type of the t message.</typeparam>
        /// <param name="action">The action.</param>
        /// <returns>Case.</returns>
        public Case With<TMessage>(Action<TMessage> action)
        {
            if (!_handled && _message is TMessage)
            {
                action((TMessage) _message);
                _handled = true;
            }

            return this;
        }

        /// <summary>
        /// Defaults the specified action.
        /// </summary>
        /// <param name="action">The action.</param>
        /// <returns>IMatchResult.</returns>
        public IMatchResult Default(Action<object> action)
        {
            if (!_handled)
            {
                action(_message);
                _handled = true;
            }
            return AlwaysHandled.Instance;
        }

        /// <summary>
        /// Class AlwaysHandled.
        /// </summary>
        private class AlwaysHandled : IMatchResult
        {
            /// <summary>
            /// The instance
            /// </summary>
            public static readonly AlwaysHandled Instance = new AlwaysHandled();
            /// <summary>
            /// Prevents a default instance of the <see cref="AlwaysHandled"/> class from being created.
            /// </summary>
            private AlwaysHandled() { }
            /// <summary>
            /// Gets a value indicating whether [was handled].
            /// </summary>
            /// <value><c>true</c> if [was handled]; otherwise, <c>false</c>.</value>
            public bool WasHandled { get { return true; } }
        }
    }

    /// <summary>
    /// Class Case with returning result.
    /// </summary>
    /// <typeparam name="T">The type of return value</typeparam>
    public class Case<T> : IMatchResult
    {
        /// <summary>
        /// The _message
        /// </summary>
        private readonly object _message;
        /// <summary>
        /// The _handled
        /// </summary>
        private bool _handled;

        /// <summary>
        /// The final result of execution
        /// </summary>
        private T _result;

        /// <summary>
        /// Gets a value indicating whether [was handled].
        /// </summary>
        /// <value><c>true</c> if [was handled]; otherwise, <c>false</c>.</value>
        public bool WasHandled { get { return _handled; } }

        /// <summary>
        /// Initializes a new instance of the <see cref="Case"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        public Case(object message)
        {
            _message = message;
        }

        /// <summary>
        /// Withes the specified action.
        /// </summary>
        /// <typeparam name="TMessage">The type of the t message.</typeparam>
        /// <param name="function">The function.</param>
        /// <returns>Case.</returns>
        public Case<T> With<TMessage>(Func<T> function)
        {
            if (!_handled && _message is TMessage)
            {
                _result = function();
                _handled = true;
            }

            return this;
        }

        /// <summary>
        /// Withes the specified action.
        /// </summary>
        /// <typeparam name="TMessage">The type of the t message.</typeparam>
        /// <param name="function">The action.</param>
        /// <returns>Case.</returns>
        public Case<T> With<TMessage>(Func<TMessage, T> function)
        {
            if (!_handled && _message is TMessage)
            {
                _result = function((TMessage)_message);
                _handled = true;
            }

            return this;
        }

        /// <summary>
        /// Defaults the specified action.
        /// </summary>
        /// <param name="function">The default function.</param>
        /// <returns>The result of the matching</returns>
        public T ResultOrDefault(Func<object, T> function)
        {
            if (!_handled)
            {
                _result = function(_message);
                _handled = true;
            }

            return _result;
        }
    }


}

