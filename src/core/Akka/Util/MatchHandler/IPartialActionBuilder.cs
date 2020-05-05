//-----------------------------------------------------------------------
// <copyright file="IPartialActionBuilder.cs" company="Akka.NET Project">
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
    internal interface IPartialActionBuilder
    {
        /// <summary>
        /// Builds the specified delegate and arguments to a <see cref="PartialAction{T}"/>
        /// <para>If the number of arguments are 0, the delegate should be a <see cref="Func{T}">Func&lt;<typeparamref name="T"/>,bool&gt;</see></para>
        /// <para>If the number of arguments are 1, the delegate should be a <see cref="Func{T,T1}">Func&lt;<typeparamref name="T"/>,T1,bool&gt;</see></para>
        /// <para>...</para>
        /// <para>If the number of arguments are n, the delegate should be a Func&lt;<typeparamref name="T"/>,T1,...,Tn,bool&gt;</para>
        /// <para>The maximum number of arguments i.e. n in the above example is therefore <see cref="PartialActionBuilder.MaxNumberOfArguments"/>=14</para>
        /// <para>Given a delegate deleg of type Func&lt;<typeparamref name="T"/>,T1,...,Tn,bool&gt; and args [a_1,...a_n] then 
        /// the delegate corresponding to this code is returned:
        /// <example>(value) => deleg(value,a_1, ..., a_n)</example>
        /// </para>
        /// </summary>
        /// <typeparam name="T">The type of the value parameter in to the returned <see cref="PartialAction{T}"/></typeparam>
        /// <param name="handlerAndArgs">The handler, i.e. a Func&lt;<typeparamref name="T"/>,T1,...,Tn,bool&gt; and arguments [a_1,...a_n].</param>
        /// <returns>Returns a <see cref="PartialAction{T}"/> that calls the delegate with the arguments.</returns>
        PartialAction<T> Build<T>(CompiledMatchHandlerWithArguments handlerAndArgs);
    }
}

