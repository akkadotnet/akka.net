using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon
{
    public interface IMessage
    {
    }

    public static class MessageExtensions
    {
        public static Case Match(this IMessage self)
        {
            return new Case(self);
        }
    }

    public class Case
    {
        private bool _handled = false;
        private IMessage _message;
        public Case(IMessage message)
        {
            _message = message;
        }

        public Case With<TMessage>(Action<TMessage> action) where TMessage : IMessage
        {
            if (!_handled && _message is TMessage)
            {
                action((TMessage)_message);
                _handled = true;
            }

            return this;
        }

        public void Default(Action<IMessage> action)
        {
            if (!_handled)
            {
                action(_message);
                _handled = true;
            }
        }
    }
}
