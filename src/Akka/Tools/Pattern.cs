﻿using System;

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

    public class Case
    {
        private readonly object _message;
        private bool _handled;

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

        public void Default(Action<object> action)
        {
            if (!_handled)
            {
                action(_message);
                _handled = true;
            }
        }
    }
}