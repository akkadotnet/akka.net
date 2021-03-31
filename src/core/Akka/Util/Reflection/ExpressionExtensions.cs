//-----------------------------------------------------------------------
// <copyright file="ExpressionExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Akka.Util.Reflection
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    public static class ExpressionExtensions
    {
        /// <summary>
        /// Fetches constructor arguments from a <see cref="NewExpression"/>.
        /// </summary>
        /// <param name="newExpression">The <see cref="NewExpression"/> used typically to create an actor.</param>
        /// <returns>The constructor type and arguments</returns>
        public static object[] GetArguments(this NewExpression newExpression)
        {
            return newExpression.ParseExpressionArgs(); 
            
        }
    }
    internal static class ExpressionBasedParser
    {
        private static readonly ConcurrentDictionary<ConstructorInfo, string[]>
            paramNameDictionary =
                new ConcurrentDictionary<ConstructorInfo, string[]>();

        private static readonly Type _objectType = typeof(object);

        private static readonly Type _multicastDelegateType =
            typeof(MulticastDelegate);
        
        public static object[] ParseExpressionArgs(
            this NewExpression newExpr)
        {
            var argProv = newExpr.Arguments;
            var argCount = argProv.Count;
            return ParseCallArgs(argCount, argProv);
        }

        /// <summary>
        /// Parses the arguments for the method call contained in the expression.
        /// </summary>
        private static object[] ParseCallArgs(int argCount,
            ReadOnlyCollection<Expression> argProv)
        {
            //ConstantExpression constExpr;
            object[] _jobArgs = new object[argCount];
            for (int i = 0; i < argCount; i++)
            {
                var theArg = argProv[i];
                //object val = null;
                try
                {

                    //constExpr = theArg as ConstantExpression;
                    if (theArg.NodeType == ExpressionType.Constant)
                    {
                        //Happy Case.
                        //If constant, no need for invokes,
                        //or anything else
                        _jobArgs[i] = ((ConstantExpression)theArg).Value;
                    }
                    else
                    {
                        if (theArg is MemberExpression _memArg)
                        {
                            if (_memArg.Expression is ConstantExpression ce)
                            {
                                //We don't need .Convert() here because for better or worse
                                //GetValue will box for us.
                                if (_memArg.Member.MemberType == MemberTypes.Field)
                                {
                                    _jobArgs[i] =
                                        ((FieldInfo)_memArg.Member).GetValue(
                                            (ce).Value);
                                    theArg = null;
                                }
                                else if (_memArg.Member.MemberType == MemberTypes.Property)
                                {
                                    _jobArgs[i] =
                                        ((PropertyInfo)_memArg.Member).GetValue(
                                            (ce).Value);
                                    theArg = null;
                                }
                            }
                        }


                        if (theArg != null)
                        {
                            //If we are dealing with a Valuetype,
                            //we need a convert here.
                            _jobArgs[i] = CompileExprWithConvert(Expression
                                    .Lambda<Func<object>>(
                                        ConvertIfNeeded(theArg)))
                                .Invoke();
                        }
                    }
                }
                catch (Exception exception)
                {
                    //Fallback. Do the worst way and compile.
                    try
                    {
                        object fallbackVal;
                        {
                            _jobArgs[i] = Expression.Lambda(
                                    Expression.Convert(theArg, _objectType)
                                )
                                .Compile().DynamicInvoke();
                        }

                    }

                    catch (Exception ex)
                    {
                        throw new ArgumentException(
                            "Couldn't derive value from Expression! Please use variables whenever possible",
                            ex);
                    }
                }
            }

            return _jobArgs;
        }
        

        private static Expression ConvertIfNeeded(Expression toConv)
        {
            Type retType = null;
            if (toConv.NodeType == ExpressionType.Lambda)
            {
                retType = TraverseForType(toConv.Type.GetGenericArguments()
                    .LastOrDefault());
            }
            else
            {
                retType = toConv.Type;
            }

            if (retType?.BaseType == _objectType)
            {
                return toConv;
            }
            else
            {
                return Expression.Convert(toConv, _objectType);
            }
        }

        private static Type TraverseForType(Type toConv)
        {
            if (toConv == null)
            {
                return null;
            }
            else if (toConv == _multicastDelegateType)
            {
                //I don't think this should happen in sane usage, but let's cover it.
                return (TraverseForType(toConv.GetGenericArguments().LastOrDefault()));
            }
            else
            {
                return toConv;
            }
        }
        private static T CompileExprWithConvert<T>(Expression<T> lambda) where T : class
        {
            return lambda.Compile();
        }
    }
}

