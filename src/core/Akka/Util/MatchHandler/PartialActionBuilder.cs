//-----------------------------------------------------------------------
// <copyright file="PartialActionBuilder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Tools.MatchHandler
{
    public class PartialActionBuilder : IPartialActionBuilder
    {
        /// <summary>
        /// The maximum number of arguments=15 not including the obligatory first value argument in a partial action. 
        /// 16 is the maximum number of args in a Func, see <see cref="Func{T1,T2,T3,T4,T5,T6,T7,T8,T9,T10,T11,T12,T13,T14,T15,T16,TResult}"/>
        /// </summary>
        public const int MaxNumberOfArguments = 15;

        private static readonly Type _objectArrayType = typeof(object[]);
        private readonly static Type[] _types = new[]
		{
			typeof(PartialHandlerArgumentsCapture<>),	
			typeof(PartialHandlerArgumentsCapture<,>),		
			typeof(PartialHandlerArgumentsCapture<,,>),		
			typeof(PartialHandlerArgumentsCapture<,,,>),		
			typeof(PartialHandlerArgumentsCapture<,,,,>),		
			typeof(PartialHandlerArgumentsCapture<,,,,,>),		
			typeof(PartialHandlerArgumentsCapture<,,,,,,>),		
			typeof(PartialHandlerArgumentsCapture<,,,,,,,>),		
			typeof(PartialHandlerArgumentsCapture<,,,,,,,,>),		
			typeof(PartialHandlerArgumentsCapture<,,,,,,,,,>),		
			typeof(PartialHandlerArgumentsCapture<,,,,,,,,,,>),		
			typeof(PartialHandlerArgumentsCapture<,,,,,,,,,,,>),		
			typeof(PartialHandlerArgumentsCapture<,,,,,,,,,,,,>),		
			typeof(PartialHandlerArgumentsCapture<,,,,,,,,,,,,,>),		
			typeof(PartialHandlerArgumentsCapture<,,,,,,,,,,,,,,>),		
			typeof(PartialHandlerArgumentsCapture<,,,,,,,,,,,,,,,>),		
		};

        public PartialAction<T> Build<T>(CompiledMatchHandlerWithArguments handlerAndArgs)
        {
            var arguments = handlerAndArgs.DelegateArguments;
            var handler = handlerAndArgs.CompiledDelegate;
            var numberOfArguments = arguments.Length; //This is except the required value parameter
            if(numberOfArguments > MaxNumberOfArguments)
                throw new ArgumentException(string.Format("Too many arguments. Max {0} arguments allowed.", MaxNumberOfArguments));
            var baseType = _types[numberOfArguments];
            var argumentTypes = new Type[numberOfArguments +1];
            argumentTypes[0] = typeof(T);
            for(var i = 0; i < numberOfArguments;)
            {
                var argumentType = arguments[i].GetType();
                i++;
                argumentTypes[i] = argumentType;
            }
            var type = baseType.MakeGenericType(argumentTypes);
            var argsCatcher = (IPartialHandlerArgumentsCapture<T>) Activator.CreateInstance(type);
            argsCatcher.Initialize(handler, arguments);
            return argsCatcher.Handle;           
        }
    }
}

