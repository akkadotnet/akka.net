using System;

namespace Akka
{
    public static class PatternMatch
    {
        //[Obsolete("This is horribly slow, should be replaced with standard 'is' checks",false)]
        public static Case Match(this object target)
        {
            return new Case(target);
        }
    }

    public interface IMatchResult
    {
        bool WasHandled { get; }
    }

    public class Case : IMatchResult
    {
        private readonly object _message;
        private bool _handled;
        public bool WasHandled { get { return _handled; } }

        public Case(object message)
        {
            _message = message;
        }

        public Case With<TMessage>(Action action)
        {
            if (!_handled && _message is TMessage)
            {
                action();
                _handled = true;
            }

            return this;
        }

        public Case With<TMessage>(Action<TMessage> action)
        {
            if (!_handled && _message is TMessage)
            {
                action((TMessage) _message);
                _handled = true;
            }

            return this;
        }

        public IMatchResult Default(Action<object> action)
        {
            if (!_handled)
            {
                action(_message);
                _handled = true;
            }
            return AlwaysHandled.Instance;
        }

        private class AlwaysHandled : IMatchResult
        {
            public static readonly AlwaysHandled Instance = new AlwaysHandled();
            private AlwaysHandled() { }
            public bool WasHandled { get { return true; } }
        }
    }
}