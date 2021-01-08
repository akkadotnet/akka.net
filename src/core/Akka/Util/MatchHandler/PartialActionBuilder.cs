//-----------------------------------------------------------------------
// <copyright file="PartialActionBuilder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Tools.MatchHandler
{
    /// <summary>
    /// TBD
    /// </summary>
    internal class PartialActionBuilder : IPartialActionBuilder
    {
        /// <summary>
        /// The maximum number of arguments=15 not including the obligatory first value argument in a partial action. 
        /// 16 is the maximum number of args in a Func, see <see cref="Func{T1,T2,T3,T4,T5,T6,T7,T8,T9,T10,T11,T12,T13,T14,T15,T16,TResult}"/>
        /// </summary>
        public const int MaxNumberOfArguments = 15;

        private static readonly Type _objectArrayType = typeof(object[]);
        private static readonly Type[] _types = new[]
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
            typeof(PartialHandlerArgumentsCapture<,,,,,,,,,,,,,,,>)
        };

        /// <summary>
        /// Builds the specified delegate and arguments to a <see cref="PartialAction{T}" /><para>If the number of arguments are 0, the delegate should be a <see cref="Func{T}">Func&lt;<typeparamref name="T" />,bool&gt;</see></para><para>If the number of arguments are 1, the delegate should be a <see cref="Func{T,T1}">Func&lt;<typeparamref name="T" />,T1,bool&gt;</see></para><para>...</para><para>If the number of arguments are n, the delegate should be a Func&lt;<typeparamref name="T" />,T1,...,Tn,bool&gt;</para><para>The maximum number of arguments i.e. n in the above example is therefore <see cref="PartialActionBuilder.MaxNumberOfArguments" />=14</para><para>Given a delegate deleg of type Func&lt;<typeparamref name="T" />,T1,...,Tn,bool&gt; and args [a_1,...a_n] then
        /// the delegate corresponding to this code is returned:
        /// <example>(value) =&gt; deleg(value,a_1, ..., a_n)</example></para>
        /// </summary>
        /// <typeparam name="T">The type of the value parameter in to the returned <see cref="PartialAction{T}" /></typeparam>
        /// <param name="handlerAndArgs">The handler, i.e. a Func&lt;<typeparamref name="T" />,T1,...,Tn,bool&gt; and arguments [a_1,...a_n].</param>
        /// <returns>
        /// Returns a <see cref="PartialAction{T}" /> that calls the delegate with the arguments.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if the number of arguments in the given <paramref name="handlerAndArgs"/> exceeds the configured <see cref="MaxNumberOfArguments"/>.
        /// </exception>
        public PartialAction<T> Build<T>(CompiledMatchHandlerWithArguments handlerAndArgs)
        {
            var arguments = handlerAndArgs.DelegateArguments;
            var handler = handlerAndArgs.CompiledDelegate;
            var numberOfArguments = arguments.Length; //This is except the required value parameter
            if(numberOfArguments > MaxNumberOfArguments)
                throw new ArgumentException($"Too many arguments. Max {MaxNumberOfArguments} arguments allowed.", nameof(handlerAndArgs));
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
