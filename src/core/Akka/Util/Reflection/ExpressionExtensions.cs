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
    /// TBD
    /// </summary>
    public static class ExpressionExtensions
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="newExpression">TBD</param>
        /// <returns>TBD</returns>
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
            object[] _jobArgs = new object[argCount];
            for (int i = 0; i < argCount; i++)
            {
                var theArg = argProv[i];
                object val = null;
                try
                {
                    if (theArg is ConstantExpression _theConst)
                    {
                        //Happy Case.
                        //If constant, no need for invokes,
                        //or anything else
                        val = _theConst.Value;
                    }
                    else
                    {
                        bool memSet = false;
                        
                            if (theArg is MemberExpression _memArg)
                            {
                                if (_memArg.Expression is ConstantExpression c)
                                {
                                    if (_memArg.Member is FieldInfo f)
                                    {
                                        val = f.GetValue(c.Value);
                                        memSet = true;
                                    }
                                    else if (_memArg.Member is PropertyInfo p)
                                    {
                                        val = p.GetValue(c.Value);
                                        memSet = true;
                                    }
                                }
                            }
                        

                        if (memSet == false)
                        {
                            //If we are dealing with a Valuetype,
                            //we need a convert here.
                            val = CompileExprWithConvert(Expression
                                    .Lambda<Func<object>>(
                                        ConvertIfNeeded(theArg)))
                                .Invoke();
                        }
                    }

                    _jobArgs[i] = val;
                }
                catch (Exception exception)
                {
                    //Fallback. Do the worst way and compile.
                    try
                    {
                        object fallbackVal;
                        {
                            fallbackVal = Expression.Lambda(
                                    Expression.Convert(theArg, _objectType)
                                )
                                .Compile().DynamicInvoke();
                        }
                        _jobArgs[i] = fallbackVal;
                    }

                    catch (Exception ex)
                    {
                        throw new ParseArgumentException(
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
                return toConv.GetType();
            }
        }
        private static T CompileExprWithConvert<T>(Expression<T> lambda) where T : class
        {
            return lambda.Compile();
        }
    }
    public class ParseArgumentException : Exception
    {
        public ParseArgumentException(string message, Exception ex):base(message,ex)
        {
            
        }
    }
}

