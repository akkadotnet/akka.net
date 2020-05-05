//-----------------------------------------------------------------------
// <copyright file="MatchExpressionBuilder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Akka.Tools.MatchHandler
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    internal class MatchExpressionBuilder<T> : IMatchExpressionBuilder
    {
        //See the end of file a description of what this class does

        private const int MaxNumberOfInlineParameters = PartialActionBuilder.MaxNumberOfArguments;
        private static readonly Type _inputItemType = typeof(T);
        // ReSharper disable once StaticFieldInGenericType
        private static readonly ParameterExpression _extraArgsArrayParameter = Expression.Parameter(typeof(object[]), "extraArgsArray");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="typeHandlers">TBD</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// This exception is thrown if the an unknown <see cref="PredicateAndHandler.HandlerKind"/> is contained
        /// in a <see cref="TypeHandler.Handlers"/> in the given <paramref name="typeHandlers"/>.
        /// </exception>
        /// <returns>TBD</returns>
        public MatchExpressionBuilderResult BuildLambdaExpression(IReadOnlyList<TypeHandler> typeHandlers)
        {
            var arguments = typeHandlers.SelectMany(h => h.GetArguments()).ToList();
            //See the end of this file for a description of what kind of code we generate
            var inputParameter = Expression.Parameter(_inputItemType, "input");
            var parametersAndArguments = DecorateHandlerAndPredicateExpressions(arguments, inputParameter);
            var parameters = parametersAndArguments.Item1;
            var argumentValues = parametersAndArguments.Item2;

            //Create the return label and the context object
            var returnTarget = Expression.Label(typeof(bool), "return");

            //Lets build the body
            var mainBodyExpressions = new List<Expression>();

            //We will cast the input variable to different types, i.g.:
            //   var stringInput = input as string;
            //We store every created variable so that, if a later handler handles strings, we already have it casted.
            var castedInputVariables = new Dictionary<Type, ParameterExpression>
            {
                {_inputItemType, inputParameter}
            };

            //Loop over all handlers
            var numberOfTypeHandlers = typeHandlers.Count;
            for(int i = 0; i < numberOfTypeHandlers; i++)
            {
                var typeHandler = typeHandlers[i];
                var handlesType = typeHandler.HandlesType;      //This is the type the handler handles
                if(handlesType == _inputItemType)
                {
                    //No need to check that the message can be casted to an object. Just add the handlers code to the body
                    //Next we'll create the rest of the if-body, i.e. the handlers code
                    AddCodeForHandlers(typeHandler.Handlers, mainBodyExpressions, inputParameter, returnTarget, inputParameter);
                }
                else
                {
                    if(handlesType.GetTypeInfo().IsValueType)
                    {
                        //For value types we cannot use as-operator and check for nu, s0 we create the following code:
                        //  if(inputVariable is HandlesType)
                        //  {
                        //    var castedVariable = (HandlesType) inputVariable;
                        //       -Handlers code-
                        //  }
                        var ifBodyExpressions = new List<Expression>();

                        //Create:  var castedValueVariable = (HandlesType) inputVariable;
                        var castedVariable = Expression.Variable(handlesType);
                        var assignment = Expression.Assign(castedVariable, Expression.Convert(inputParameter, handlesType));
                        ifBodyExpressions.Add(assignment);

                        //Next we'll create the rest of the if-body, i.e. the handlers code
                        AddCodeForHandlers(typeHandler.Handlers, ifBodyExpressions, castedVariable, returnTarget, inputParameter);

                        //Create the if-expression
                        var ifExpression = Expression.IfThen(
                            Expression.TypeIs(inputParameter, handlesType),
                            Expression.Block(new[] { castedVariable }, ifBodyExpressions));
                        mainBodyExpressions.Add(ifExpression);
                    }
                    else
                    {
                        //For reference type we'll cast the input using the as-operator and then check for null
                        //  var castedVariable = inputVariable as HandlesType;
                        //  if(castedVariable != null)
                        //  {
                        //     -Handler code-
                        //  }

                        //We might have a variable of the correct type already, so use it in that case
                        if(!castedInputVariables.TryGetValue(handlesType, out var castedVariable))
                        {
                            //The variable did not exist, create and store it:
                            //  var castedVariable = inputParameter as HandlesType;
                            castedVariable = Expression.Variable(handlesType);
                            var assignment = Expression.Assign(castedVariable, Expression.TypeAs(inputParameter, handlesType));
                            castedInputVariables.Add(handlesType, castedVariable);
                            mainBodyExpressions.Add(assignment);
                        }

                        //Next we'll create the rest of the if-body, i.e. the handlers code
                        var ifBodyExpressions = new List<Expression>();
                        AddCodeForHandlers(typeHandler.Handlers, ifBodyExpressions, castedVariable, returnTarget, inputParameter);

                        //Add expression: if(messageVariable!=null) { handlerExpressions }
                        var ifExpression = Expression.IfThen(
                            Expression.NotEqual(castedVariable, Expression.Constant(null)),
                            Expression.Block(ifBodyExpressions));
                        mainBodyExpressions.Add(ifExpression);

                    }
                }

            }
            //Add the goto-label that is used for return statements
            mainBodyExpressions.Add(Expression.Label(returnTarget, Expression.Constant(false)));

            //Extract all declared variables (ignoring messageParameter which will be declared in the head of the delegate)
            var variables = castedInputVariables.Values.Where(exp => exp != inputParameter);

            //Create the body, and declare all variables that was added to the mainBodyExpressions.
            var body = Expression.Block(variables, mainBodyExpressions);

            //Create the Lambda expressions.
            var lambdaExpression = Expression.Lambda(body, parameters);
            return new MatchExpressionBuilderResult(lambdaExpression, argumentValues);
        }

        private static (ParameterExpression[], object[]) DecorateHandlerAndPredicateExpressions(IReadOnlyList<Argument> arguments, ParameterExpression inputParameter)
        {
            //Warning: This is using the same algorithm as CreateArgumentValuesArray.
            //         Any updates in this should be made in CreateArgumentValuesArray as well.
            //
            //If we only have a few arguments, the parameters will be:
            //    (arguments_0, arguments_1)
            //If we have more than we can fit, we'll create an array at the end and add the last ones there
            //    (arguments_0, ..., arguments_n, extraArgsArray)
            int numberOfInlineParameters = arguments.Count;
            var numberOfExtraArgs = numberOfInlineParameters - MaxNumberOfInlineParameters;
            object[] argumentValues;
            ParameterExpression[] parameters;
            object[] extraArgsValues = null;
            if(numberOfExtraArgs > 0)
            {
                //We have at least one extra argument. We have to make room for extraArgsArray parameter
                numberOfInlineParameters = MaxNumberOfInlineParameters - 1;
                numberOfExtraArgs++;    //Since we made room for the extraArgsArray we have to move one of the args to extraArgsArray

                argumentValues = new object[MaxNumberOfInlineParameters];
                extraArgsValues = new object[numberOfExtraArgs];
                argumentValues[numberOfInlineParameters] = extraArgsValues;
                parameters = new ParameterExpression[MaxNumberOfInlineParameters + 1];
                parameters[numberOfInlineParameters + 1] = _extraArgsArrayParameter;
            }
            else
            {
                argumentValues = new object[numberOfInlineParameters];
                parameters = new ParameterExpression[numberOfInlineParameters + 1];
            }
            parameters[0] = inputParameter;
            for(var i = 0; i < numberOfInlineParameters; i++)
            {
                var iPlus1 = i + 1;
                var argument = arguments[i];
                var argumentValue = argument.Value;
                argumentValues[i] = argumentValue;
                var parameter = Expression.Parameter(argumentValue.GetType(), "arg" + iPlus1);
                parameters[iPlus1] = parameter;
                if(argument.ValueIsActionOrFunc)
                    argument.PredicateAndHandler.ActionOrFuncExpression = parameter;
                else
                    argument.PredicateAndHandler.PredicateExpression = parameter;
            }
            if(numberOfExtraArgs > 0)
            {
                var argsIndex = numberOfInlineParameters;
                for(var i = 0; i < numberOfExtraArgs; argsIndex++, i++)
                {
                    var argument = arguments[argsIndex];
                    var argumentValue = argument.Value;
                    extraArgsValues[i] = argumentValue;
                    var expression = Expression.Convert(Expression.ArrayIndex(_extraArgsArrayParameter, Expression.Constant(i)), argumentValue.GetType());
                    if(argument.ValueIsActionOrFunc)
                        argument.PredicateAndHandler.ActionOrFuncExpression = expression;
                    else
                        argument.PredicateAndHandler.PredicateExpression = expression;
                }
            }
            return (parameters, argumentValues);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="arguments">TBD</param>
        /// <returns>TBD</returns>
        public object[] CreateArgumentValuesArray(IReadOnlyList<Argument> arguments)
        {
            //Warning: This is a stripped down version, using the same algorithm as DecorateHandlerAndPredicateExpressions.
            //         Any updates in this should be made in DecorateHandlerAndPredicateExpressions as well.
            var numberOfArguments = arguments.Count;
            if(numberOfArguments > MaxNumberOfInlineParameters)
            {
                //We have at least one extra argument. We have to make room for extraArgsArray parameter
                const int numberOfInlineParameters = MaxNumberOfInlineParameters - 1;
                var numberOfExtraArgs = numberOfArguments - numberOfInlineParameters;

                var argumentValues = new object[MaxNumberOfInlineParameters];
                int i = 0;
                for(; i < numberOfInlineParameters; i++)
                {
                    var argument = arguments[i];
                    argumentValues[i] = argument.Value;
                }
                var extraArgsValues = new object[numberOfExtraArgs];
                argumentValues[i] = extraArgsValues;
                for(var j = 0; j < numberOfExtraArgs; j++, i++)
                {
                    var argument = arguments[i];
                    extraArgsValues[j] = argument.Value;
                }
                return argumentValues;
            }
            else
            {
                var argumentValues = new object[numberOfArguments];
                for(var i = 0; i < numberOfArguments; i++)
                {
                    var argument = arguments[i];
                    argumentValues[i] = argument.Value;
                }
                return argumentValues;
            }

        }


        // Generates code for predicateAndHandlers and adds it to bodyExpressions
        private static void AddCodeForHandlers(List<PredicateAndHandler> predicateAndHandlers, List<Expression> bodyExpressions, Expression castedValueExpression, LabelTarget returnTarget, Expression inputValueExpression)
        {
            foreach(var predicateAndHandler in predicateAndHandlers)
            {
                var valueExpression = predicateAndHandler.HandlerFirstArgumentShouldBeBaseType ? inputValueExpression : castedValueExpression;
                switch(predicateAndHandler.HandlerKind)
                {
                    case HandlerKind.Action:
                        AddActionHandlerExpressions(predicateAndHandler, bodyExpressions, valueExpression, returnTarget);
                        break;
                    case HandlerKind.ActionWithPredicate:
                        AddActionHandlerWithPredicateExpressions(predicateAndHandler, bodyExpressions, valueExpression, returnTarget);
                        break;
                    case HandlerKind.Func:
                        AddFuncHandlerExpressions(predicateAndHandler, bodyExpressions, valueExpression, returnTarget);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            $"This should not happen. The value {typeof(HandlerKind)}.{predicateAndHandler.HandlerKind} is a new enum value that has been added without updating the code in this method.");
                }
            }
        }



        private static void AddActionHandlerExpressions(PredicateAndHandler handler, List<Expression> body, Expression argumentExpression, LabelTarget returnTarget)
        {
            //Add the action as an argument, and get the expression for retrieving it

            //Adds this code to the body:
            //    action(arg);
            //    return true;
            body.Add(Expression.Invoke(handler.ActionOrFuncExpression, argumentExpression));
            body.Add(Expression.Return(returnTarget, Expression.Constant(true)));
        }

        private static void AddActionHandlerWithPredicateExpressions(PredicateAndHandler handler, List<Expression> body, Expression argumentExpression, LabelTarget returnTarget)
        {

            //Add this code to the body:
            //    if(predicate(arg))
            //    {
            //      action(arg);
            //      return true;
            //    }
            body.Add(
                Expression.IfThen(
                    Expression.Invoke(handler.PredicateExpression, argumentExpression),
                    Expression.Block(
                        Expression.Invoke(handler.ActionOrFuncExpression, argumentExpression),
                        Expression.Return(returnTarget, Expression.Constant(true))
                        )));
        }

        private static void AddFuncHandlerExpressions(PredicateAndHandler handler, List<Expression> body, Expression argumentExpression, LabelTarget returnTarget)
        {
            //Add this code to the body:
            //    if(func(castedValue))
            //    {
            //      return true;
            //    }
            body.Add(
                Expression.IfThen(
                    Expression.Invoke(handler.ActionOrFuncExpression, argumentExpression),
                        Expression.Return(returnTarget, Expression.Constant(true))
                        ));
        }


        //Basically this class build this expression tree:
        //   bool Handler(T input, Type0 arg0, ..., Typen argn, object[] extra)
        //   {
        //      //BODY
        //      return: //label
        //   }
        //
        //We try to inline as many argument as function parameters as possible, the rest goes into the extra array.
        //The arguments are the predicates and handler methods captured by the builder.
        //   Example 1:
        //      Receive<string>(s=>s.Length==5, s=>DoSomething(s));
        //      Receive<string>(s=>DoSomethingElse(s));
        //      Receive<int>(i=>DoSomethingElseWithInt(i));
        //Will result in the following code:
        //   Example 2:
        //      bool Handler(T input, Predicate<string> p1, Action<string> a1, Action<string> a2, Action<int> a3)
        //      {
        //          var s = input as string;
        //          if(s != null)
        //          {
        //              if(p1(s))
        //              {
        //                  a1(s);
        //                  return true;
        //              }
        //              a2(s);
        //              return true;
        //          }
        //          if(input is int)
        //          {
        //              var i = (int)input;
        //              a3(i);
        //              return true;
        //          }
        //          return false;
        //      }
        //Typically the code in Example 1 is called in the constructor of an Actor, and although the code looks the same,
        //the actions and predicates sent in to receive actually are new instances.
        //The reason we capture the predicates and actions, instead of calling them directly, is that the code 
        //in Example 2, can be compiled, cached and reused no matter the predicates and actions.
    }
}

