//-----------------------------------------------------------------------
// <copyright file="MatchBuilder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace Akka.Tools.MatchHandler
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TItem">TBD</typeparam>
    internal class MatchBuilder<TItem>
    {
        //This class works by collecting all handlers.
        //By creating a signature, made up of all types [Type], and types-of-handlers [HandlerKind], we can use the same code
        //for all with the same signature.
        //
        //These two builders will create the same signature (given that these are the only match calls made):
        //    builder1.Match<string>(s=>F(s) , s=>s=="");
        //    builder2.Match<string>(s=>G(s) , s=>s!="");
        //The signature will be [ typeof(String), HandlerKind.ActionWithPredicate ] for both, and will yield pseudo code that looks like:
        //    bool Matcher(o, action, predicate)
        //        if(o is string && predicate((string) s)) { action((string) s); return true; }
        //        return false;
        //
        //Since both share the same signature they can both use the same expression tree, i.e. the same Matcher-function
        //    
        //Actions, Funcs and Predicates that are specified by the user in Match-calls are added to lists, like a closure.
        //For the example above the captured variables for the two builders are:
        //    builder1:  action = s=>F(s); predicate = s=>s==""
        //    builder2:  action = s=>G(s); predicate = s=>s!=""

        private static readonly Type _itemType = typeof(TItem);
        private readonly List<TypeHandler> _typeHandlers = new List<TypeHandler>(); //Contains all handlers, with the handled types and predicates
        private readonly List<Argument> _arguments = new List<Argument>();                //Contains Actions,Predicates and Funcs that has been added
        private readonly List<object> _signature = new List<object>();
        private readonly IMatchCompiler<TItem> _compiler;
        private State _state;


        /// <summary>
        /// Initializes a new instance of the <see cref="MatchBuilder{TItem}"/> class.
        /// </summary>
        /// <param name="compiler">TBD</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown if the given <paramref name="compiler"/> is undefined.
        /// </exception>
        public MatchBuilder(IMatchCompiler<TItem> compiler)
        {
            if(compiler == null) throw new ArgumentNullException(nameof(compiler), "Compiler cannot be null");
            _compiler = compiler;
        }

        /// <summary>
        /// Adds a handler that is called if the item being matched is of type <typeparamref name="T"/>
        /// and <paramref name="shouldHandle"/>, if it has been specified, returns <c>true</c>.
        /// <remarks>Note that if a previous added handler handled the item, this <paramref name="handler"/> will not be invoked.</remarks>
        /// </summary>
        /// <typeparam name="T">The type that it must match in order for <paramref name="handler"/> to be called.</typeparam>
        /// <param name="handler">The handler that is invoked when everything matches.</param>
        /// <param name="shouldHandle">An optional predicate to test if the item matches. If it returns <c>true</c> the <paramref name="handler"/> is invoked.</param>
        /// <exception cref="InvalidOperationException">
        /// This exception is thrown if a handler that catches all messages has been added or a partial action has already been built. 
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// This exception is thrown if the current state is unknown.
        /// </exception>
        public void Match<T>(Action<T> handler, Predicate<T> shouldHandle = null) where T : TItem
        {
            EnsureCanAdd();
            var handlesType = typeof(T);
            AddHandler(handlesType, PredicateAndHandler.CreateAction(handler, shouldHandle));
            if(handlesType == _itemType && shouldHandle == null)
                _state = State.MatchAnyAdded;
        }

        /// <summary>
        /// Adds a handler that is called if the item being matched is of type <paramref name="handlesType"/>
        /// and <paramref name="shouldHandle"/>, if it has been specified, returns <c>true</c>.
        /// <remarks>Note that if a previous added handler handled the item, this <paramref name="handler"/> will not be invoked.</remarks>
        /// </summary>
        /// <param name="handlesType">The type that it must match in order for <paramref name="handler"/> to be called.</param>
        /// <param name="handler">The handler that is invoked when everything matches.</param>
        /// <param name="shouldHandle">An optional predicate to test if the item matches. If it returns <c>true</c> the <paramref name="handler"/> is invoked.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if the given <paramref name="handler"/> cannot handle the given <paramref name="handlesType"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// This exception is thrown if the current state is unknown.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// This exception is thrown if a handler that catches all messages has been added or a partial action has already been built. 
        /// </exception>
        public void Match(Type handlesType, Action<TItem> handler, Predicate<TItem> shouldHandle = null)
        {
            EnsureCanAdd();
            EnsureCanHandleType(handlesType);
            AddHandler(handlesType, PredicateAndHandler.CreateAction(handler, shouldHandle, true));
            if(handlesType == _itemType && shouldHandle == null)
                _state = State.MatchAnyAdded;
        }

        /// <summary>
        /// Adds a handler that is called if the item being matched is of type <typeparamref name="T"/>.
        /// The handler should return <c>true</c> if the item sent in matched and was handled.
        /// <remarks>Note that if a previous added handler handled the item, this <paramref name="handler"/> will not be invoked.</remarks>
        /// </summary>
        /// <typeparam name="T">The type that it must match in order for <paramref name="handler"/> to be called.</typeparam>
        /// <param name="handler">The handler that is invoked. It should return <c>true</c> if the item sent in matched and was handled.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// This exception is thrown if the current state is unknown.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// This exception is thrown if a handler that catches all messages has been added or a partial action has already been built. 
        /// </exception>
        public void Match<T>(Func<T, bool> handler) where T : TItem
        {
            EnsureCanAdd();
            var handlesType = typeof(T);
            AddHandler(handlesType, PredicateAndHandler.CreateFunc(handler));
        }

        /// <summary>
        /// Adds a handler that is called if the item being matched is of type <paramref name="handlesType"/>.
        /// The handler should return <c>true</c> if the item sent in matched and was handled.
        /// <remarks>Note that if a previous added handler handled the item, this <paramref name="handler"/> will not be invoked.</remarks>
        /// </summary>
        /// <param name="handlesType">The type that it must match in order for <paramref name="handler"/> to be called.</param>
        /// <param name="handler">The handler that is invoked. It should return <c>true</c> if the item sent in matched and was handled.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if the given <paramref name="handler"/> cannot handle the given <paramref name="handlesType"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// This exception is thrown if the current state is unknown.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// This exception is thrown if a handler that catches all messages has been added or a partial action has already been built. 
        /// </exception>
        public void Match(Type handlesType, Func<TItem, bool> handler)
        {
            EnsureCanAdd();
            EnsureCanHandleType(handlesType);
            AddHandler(handlesType, PredicateAndHandler.CreateFunc(handler, true));
        }

        /// <summary>
        /// Adds a handler that is invoked no matter the type the item being matched is.
        /// <remarks>Note that since this matches all items, no more handlers may be added after this one.</remarks>
        /// <remarks>Note that if a previous added handler handled the item, this <paramref name="handler"/> will not be invoked.</remarks>
        /// </summary>
        public void MatchAny(Action<TItem> handler)
        {
            Match(handler);
        }

        /// <summary>
        /// Builds all added handlers and returns a <see cref="PartialAction{TItem}"/>.
        /// </summary>
        /// <returns>Returns a <see cref="PartialAction{TItem}"/></returns>
        public PartialAction<TItem> Build()
        {
            var partialAction = _compiler.Compile(_typeHandlers, _arguments, new MatchBuilderSignature(_signature));
            _state = State.Built;
            return partialAction;
        }

#if !CORECLR
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="typeBuilder">TBD</param>
        /// <param name="methodName">TBD</param>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public void BuildToMethod(TypeBuilder typeBuilder, string methodName, MethodAttributes attributes = MethodAttributes.Public | MethodAttributes.Static)
        {
            _compiler.CompileToMethod(_typeHandlers, _arguments, new MatchBuilderSignature(_signature), typeBuilder, methodName, methodAttributes: attributes);
            _state = State.Built;
        }
#endif

        private static void EnsureCanHandleType(Type handlesType)
        {
            if(!_itemType.IsAssignableFrom(handlesType))
                throw new ArgumentException($"The specified type ({handlesType}) must implement {_itemType}", nameof(handlesType));
        }

        //Throws an exception if a MatchAny handler has been added or the partial handler has been created.
        private void EnsureCanAdd()
        {
            switch(_state)
            {
                case State.Adding:
                    return;
                case State.MatchAnyAdded:
                    throw new InvalidOperationException("A handler that catches all messages has been added. No handler can be added after that.");
                case State.Built:
                    throw new InvalidOperationException("The partial action has been built. No handler can be added after that.");
                default:
                    throw new ArgumentOutOfRangeException($"Whoa, this should not happen! Unknown state value={_state}");
            }
        }

        private void AddHandler(Type handlesType, PredicateAndHandler predicateAndHandler)
        {
            TypeHandler typeHandler;

            //if the previous handler handles the same type, we don't need an entirely new TypeHandler,
            //we can just add the handler to its' list of handlers

            if(!TryGetPreviousTypeHandlerIfItHandlesSameType(handlesType, out typeHandler))
            {
                //Either no previous handler had been added, or it handled a different type.
                //Create a new handler and store it.
                typeHandler = new TypeHandler(handlesType);
                _typeHandlers.Add(typeHandler);

                //The type is part of the signature
                _signature.Add(handlesType);
            }

            //Store the handler (action or func), the predicate the type of handler
            typeHandler.Handlers.Add(predicateAndHandler);

            //The kind of handler (action, action+predicate or fun) is part of the signature
            _signature.Add(predicateAndHandler.HandlerKind);

            //Capture the handler (action or func) and the predicate if specified
            _arguments.AddRange(predicateAndHandler.Arguments);
        }

        //If the last item in _typeHandlers handles the same type, it is returned in typeHandler
        private bool TryGetPreviousTypeHandlerIfItHandlesSameType(Type type, out TypeHandler typeHandler)
        {
            var existingNumberOfHandlers = _typeHandlers.Count;
            if(existingNumberOfHandlers > 0)
            {
                var lastHandlerInfo = _typeHandlers[existingNumberOfHandlers - 1];
                if(lastHandlerInfo.HandlesType == type)
                {
                    typeHandler = lastHandlerInfo;
                    return true;
                }
            }
            typeHandler = null;
            return false;
        }

        private enum State
        {
            Adding, MatchAnyAdded, Built
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal class MatchBuilder : MatchBuilder<object>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="compiler">TBD</param>
        public MatchBuilder(IMatchCompiler<object> compiler)
            : base(compiler)
        {
        }
    }
}

