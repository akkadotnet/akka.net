using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Util
{
    /// <summary>
    /// Builds a pattern matching delegate used for matching incoming actor
    /// messages to a specified functional pattern.
    /// </summary>
    public class PatternBuilder
    {
        /// <summary>
        /// A list of pattern matching handlers.
        /// </summary>
        private List<Expression> handlers = new List<Expression>();

        /// <summary>
        /// The default matching handler, if any.
        /// </summary>
        private Expression defaultHandler;

        /// <summary>
        /// The expression variable that determines if the matcher handled the pattern or not.
        /// </summary>
        private ParameterExpression isHandled = Expression.Parameter(typeof(bool), "isHandled");

        /// <summary>
        /// The expression parameter that holds the input to match against.
        /// </summary>
        private ParameterExpression inputMessage = Expression.Parameter(typeof(object), "inputMessage");

        private PatternBuilder() { }

        /// <summary>
        /// Adds a pattern to match against.
        /// </summary>
        /// <typeparam name="TMessage">The type of message that the pattern handler will respond to.</typeparam>
        /// <param name="handler">The action to be performed when this pattern is matched.</param>
        /// <returns>The pattern builder.</returns>
        public PatternBuilder With<TMessage>(Action<TMessage> handler)
        {
            Expression<Action<TMessage>> handlerBinder = (x) => handler(x);

            var ifIsThenHandleExpression = Expression.IfThen(
                Expression.TypeIs(inputMessage, typeof(TMessage)),
                Expression.Block(new Expression[] {
                    Expression.Invoke(handlerBinder, Expression.Convert(inputMessage, typeof(TMessage))),
                    Expression.Assign(isHandled, Expression.Constant(true))
                })
            );

            handlers.Add(ifIsThenHandleExpression);
            return this;
        }

        /// <summary>
        /// Adds a default pattern to match if no other patterns have handled the
        /// message.
        /// </summary>
        /// <param name="handler">The action to be performed when the default patter is matched.</param>
        /// <returns>The pattern builder.</returns>
        public PatternBuilder Default(Action<object> handler)
        {
            Expression<Action<object>> handlerBinder = (x) => handler(x);

            defaultHandler = Expression.IfThen(
                Expression.Not(isHandled),
                Expression.Block(new Expression[] 
                {
                    Expression.Invoke(handlerBinder, inputMessage),
                    Expression.Assign(isHandled, Expression.Constant(true))
                })
            );
            return this;
        }

        /// <summary>
        /// Compiles the pattern into a function delegate.
        /// </summary>
        /// <returns>The compiled pattern matching delegate.</returns>
        public Matcher Compile()
        {
            var allHandlers = handlers.ToList();
            if(defaultHandler != null)
            {
                allHandlers.Add(defaultHandler);
            }

            allHandlers.Add(isHandled);
            var block = Expression.Block(new ParameterExpression[] { isHandled }, allHandlers);

            return Expression.Lambda<Matcher>(block, new ParameterExpression[] { inputMessage }).Compile();
        }

        /// <summary>
        /// Starts the creation of a new functional pattern matcher.
        /// </summary>
        /// <returns>A new pattern.</returns>
        public static PatternBuilder Match()
        {
            return new PatternBuilder();
        }
    }
}
