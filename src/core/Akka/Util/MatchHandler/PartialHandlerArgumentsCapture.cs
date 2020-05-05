//-----------------------------------------------------------------------
// <copyright file="PartialHandlerArgumentsCapture.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------


using System;
using System.Collections.Generic;

namespace Akka.Tools.MatchHandler
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    internal interface IPartialHandlerArgumentsCapture<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        /// <param name="arguments">TBD</param>
        /// <returns>TBD</returns>
        void Initialize(Delegate handler, IReadOnlyList<object> arguments);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        bool Handle(T message);
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    internal sealed class PartialHandlerArgumentsCapture<T> : IPartialHandlerArgumentsCapture<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        /// <param name="arguments">TBD</param>
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            _handler = (Func<object, bool>)handler;
        }

        private Func<object, bool> _handler;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public bool Handle(T value) { return _handler(value); }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    internal sealed class PartialHandlerArgumentsCapture<T, T1> : IPartialHandlerArgumentsCapture<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        /// <param name="arguments">TBD</param>
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            _handler = (Func<object, T1, bool>)handler;
            _1 = (T1)arguments[0];
        }
        private Func<object, T1, bool> _handler;
        private T1 _1;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public bool Handle(T value) { return _handler(value, _1); }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    internal sealed class PartialHandlerArgumentsCapture<T, T1, T2> : IPartialHandlerArgumentsCapture<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        /// <param name="arguments">TBD</param>
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            _handler = (Func<object, T1, T2, bool>)handler;
            _1 = (T1)arguments[0];
            _2 = (T2)arguments[1];
        }
        private Func<object, T1, T2, bool> _handler;
        private T1 _1;
        private T2 _2;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public bool Handle(T value) { return _handler(value, _1, _2); }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    internal sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3> : IPartialHandlerArgumentsCapture<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        /// <param name="arguments">TBD</param>
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            _handler = (Func<object, T1, T2, T3, bool>)handler;
            _1 = (T1)arguments[0];
            _2 = (T2)arguments[1];
            _3 = (T3)arguments[2];
        }
        private Func<object, T1, T2, T3, bool> _handler;
        private T1 _1;
        private T2 _2;
        private T3 _3;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public bool Handle(T value) { return _handler(value, _1, _2, _3); }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    /// <typeparam name="T4">TBD</typeparam>
    internal sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4> : IPartialHandlerArgumentsCapture<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        /// <param name="arguments">TBD</param>
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            _handler = (Func<object, T1, T2, T3, T4, bool>)handler;
            _1 = (T1)arguments[0];
            _2 = (T2)arguments[1];
            _3 = (T3)arguments[2];
            _4 = (T4)arguments[3];
        }
        private Func<object, T1, T2, T3, T4, bool> _handler;
        private T1 _1;
        private T2 _2;
        private T3 _3;
        private T4 _4;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public bool Handle(T value) { return _handler(value, _1, _2, _3, _4); }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    /// <typeparam name="T4">TBD</typeparam>
    /// <typeparam name="T5">TBD</typeparam>
    internal sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4, T5> : IPartialHandlerArgumentsCapture<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        /// <param name="arguments">TBD</param>
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            _handler = (Func<object, T1, T2, T3, T4, T5, bool>)handler;
            _1 = (T1)arguments[0];
            _2 = (T2)arguments[1];
            _3 = (T3)arguments[2];
            _4 = (T4)arguments[3];
            _5 = (T5)arguments[4];
        }
        private Func<object, T1, T2, T3, T4, T5, bool> _handler;
        private T1 _1;
        private T2 _2;
        private T3 _3;
        private T4 _4;
        private T5 _5;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public bool Handle(T value) { return _handler(value, _1, _2, _3, _4, _5); }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    /// <typeparam name="T4">TBD</typeparam>
    /// <typeparam name="T5">TBD</typeparam>
    /// <typeparam name="T6">TBD</typeparam>
    internal sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4, T5, T6> : IPartialHandlerArgumentsCapture<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        /// <param name="arguments">TBD</param>
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            _handler = (Func<object, T1, T2, T3, T4, T5, T6, bool>)handler;
            _1 = (T1)arguments[0];
            _2 = (T2)arguments[1];
            _3 = (T3)arguments[2];
            _4 = (T4)arguments[3];
            _5 = (T5)arguments[4];
            _6 = (T6)arguments[5];
        }
        private Func<object, T1, T2, T3, T4, T5, T6, bool> _handler;
        private T1 _1;
        private T2 _2;
        private T3 _3;
        private T4 _4;
        private T5 _5;
        private T6 _6;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public bool Handle(T value) { return _handler(value, _1, _2, _3, _4, _5, _6); }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    /// <typeparam name="T4">TBD</typeparam>
    /// <typeparam name="T5">TBD</typeparam>
    /// <typeparam name="T6">TBD</typeparam>
    /// <typeparam name="T7">TBD</typeparam>
    internal sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4, T5, T6, T7> : IPartialHandlerArgumentsCapture<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        /// <param name="arguments">TBD</param>
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            _handler = (Func<object, T1, T2, T3, T4, T5, T6, T7, bool>)handler;
            _1 = (T1)arguments[0];
            _2 = (T2)arguments[1];
            _3 = (T3)arguments[2];
            _4 = (T4)arguments[3];
            _5 = (T5)arguments[4];
            _6 = (T6)arguments[5];
            _7 = (T7)arguments[6];
        }
        private Func<object, T1, T2, T3, T4, T5, T6, T7, bool> _handler;
        private T1 _1;
        private T2 _2;
        private T3 _3;
        private T4 _4;
        private T5 _5;
        private T6 _6;
        private T7 _7;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public bool Handle(T value) { return _handler(value, _1, _2, _3, _4, _5, _6, _7); }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    /// <typeparam name="T4">TBD</typeparam>
    /// <typeparam name="T5">TBD</typeparam>
    /// <typeparam name="T6">TBD</typeparam>
    /// <typeparam name="T7">TBD</typeparam>
    /// <typeparam name="T8">TBD</typeparam>
    internal sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4, T5, T6, T7, T8> : IPartialHandlerArgumentsCapture<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        /// <param name="arguments">TBD</param>
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            _handler = (Func<object, T1, T2, T3, T4, T5, T6, T7, T8, bool>)handler;
            _1 = (T1)arguments[0];
            _2 = (T2)arguments[1];
            _3 = (T3)arguments[2];
            _4 = (T4)arguments[3];
            _5 = (T5)arguments[4];
            _6 = (T6)arguments[5];
            _7 = (T7)arguments[6];
            _8 = (T8)arguments[7];
        }
        private Func<object, T1, T2, T3, T4, T5, T6, T7, T8, bool> _handler;
        private T1 _1;
        private T2 _2;
        private T3 _3;
        private T4 _4;
        private T5 _5;
        private T6 _6;
        private T7 _7;
        private T8 _8;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public bool Handle(T value) { return _handler(value, _1, _2, _3, _4, _5, _6, _7, _8); }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    /// <typeparam name="T4">TBD</typeparam>
    /// <typeparam name="T5">TBD</typeparam>
    /// <typeparam name="T6">TBD</typeparam>
    /// <typeparam name="T7">TBD</typeparam>
    /// <typeparam name="T8">TBD</typeparam>
    /// <typeparam name="T9">TBD</typeparam>
    internal sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4, T5, T6, T7, T8, T9> : IPartialHandlerArgumentsCapture<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        /// <param name="arguments">TBD</param>
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            _handler = (Func<object, T1, T2, T3, T4, T5, T6, T7, T8, T9, bool>)handler;
            _1 = (T1)arguments[0];
            _2 = (T2)arguments[1];
            _3 = (T3)arguments[2];
            _4 = (T4)arguments[3];
            _5 = (T5)arguments[4];
            _6 = (T6)arguments[5];
            _7 = (T7)arguments[6];
            _8 = (T8)arguments[7];
            _9 = (T9)arguments[8];
        }
        private Func<object, T1, T2, T3, T4, T5, T6, T7, T8, T9, bool> _handler;
        private T1 _1;
        private T2 _2;
        private T3 _3;
        private T4 _4;
        private T5 _5;
        private T6 _6;
        private T7 _7;
        private T8 _8;
        private T9 _9;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public bool Handle(T value) { return _handler(value, _1, _2, _3, _4, _5, _6, _7, _8, _9); }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    /// <typeparam name="T4">TBD</typeparam>
    /// <typeparam name="T5">TBD</typeparam>
    /// <typeparam name="T6">TBD</typeparam>
    /// <typeparam name="T7">TBD</typeparam>
    /// <typeparam name="T8">TBD</typeparam>
    /// <typeparam name="T9">TBD</typeparam>
    /// <typeparam name="T10">TBD</typeparam>
    internal sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : IPartialHandlerArgumentsCapture<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        /// <param name="arguments">TBD</param>
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            _handler = (Func<object, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, bool>)handler;
            _1 = (T1)arguments[0];
            _2 = (T2)arguments[1];
            _3 = (T3)arguments[2];
            _4 = (T4)arguments[3];
            _5 = (T5)arguments[4];
            _6 = (T6)arguments[5];
            _7 = (T7)arguments[6];
            _8 = (T8)arguments[7];
            _9 = (T9)arguments[8];
            _10 = (T10)arguments[9];
        }
        private Func<object, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, bool> _handler;
        private T1 _1;
        private T2 _2;
        private T3 _3;
        private T4 _4;
        private T5 _5;
        private T6 _6;
        private T7 _7;
        private T8 _8;
        private T9 _9;
        private T10 _10;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public bool Handle(T value) { return _handler(value, _1, _2, _3, _4, _5, _6, _7, _8, _9, _10); }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    /// <typeparam name="T4">TBD</typeparam>
    /// <typeparam name="T5">TBD</typeparam>
    /// <typeparam name="T6">TBD</typeparam>
    /// <typeparam name="T7">TBD</typeparam>
    /// <typeparam name="T8">TBD</typeparam>
    /// <typeparam name="T9">TBD</typeparam>
    /// <typeparam name="T10">TBD</typeparam>
    /// <typeparam name="T11">TBD</typeparam>
    internal sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> : IPartialHandlerArgumentsCapture<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        /// <param name="arguments">TBD</param>
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            _handler = (Func<object, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, bool>)handler;
            _1 = (T1)arguments[0];
            _2 = (T2)arguments[1];
            _3 = (T3)arguments[2];
            _4 = (T4)arguments[3];
            _5 = (T5)arguments[4];
            _6 = (T6)arguments[5];
            _7 = (T7)arguments[6];
            _8 = (T8)arguments[7];
            _9 = (T9)arguments[8];
            _10 = (T10)arguments[9];
            _11 = (T11)arguments[10];
        }
        private Func<object, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, bool> _handler;
        private T1 _1;
        private T2 _2;
        private T3 _3;
        private T4 _4;
        private T5 _5;
        private T6 _6;
        private T7 _7;
        private T8 _8;
        private T9 _9;
        private T10 _10;
        private T11 _11;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public bool Handle(T value) { return _handler(value, _1, _2, _3, _4, _5, _6, _7, _8, _9, _10, _11); }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    /// <typeparam name="T4">TBD</typeparam>
    /// <typeparam name="T5">TBD</typeparam>
    /// <typeparam name="T6">TBD</typeparam>
    /// <typeparam name="T7">TBD</typeparam>
    /// <typeparam name="T8">TBD</typeparam>
    /// <typeparam name="T9">TBD</typeparam>
    /// <typeparam name="T10">TBD</typeparam>
    /// <typeparam name="T11">TBD</typeparam>
    /// <typeparam name="T12">TBD</typeparam>
    internal sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> : IPartialHandlerArgumentsCapture<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        /// <param name="arguments">TBD</param>
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            _handler = (Func<object, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, bool>)handler;
            _1 = (T1)arguments[0];
            _2 = (T2)arguments[1];
            _3 = (T3)arguments[2];
            _4 = (T4)arguments[3];
            _5 = (T5)arguments[4];
            _6 = (T6)arguments[5];
            _7 = (T7)arguments[6];
            _8 = (T8)arguments[7];
            _9 = (T9)arguments[8];
            _10 = (T10)arguments[9];
            _11 = (T11)arguments[10];
            _12 = (T12)arguments[11];
        }
        private Func<object, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, bool> _handler;
        private T1 _1;
        private T2 _2;
        private T3 _3;
        private T4 _4;
        private T5 _5;
        private T6 _6;
        private T7 _7;
        private T8 _8;
        private T9 _9;
        private T10 _10;
        private T11 _11;
        private T12 _12;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public bool Handle(T value) { return _handler(value, _1, _2, _3, _4, _5, _6, _7, _8, _9, _10, _11, _12); }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    /// <typeparam name="T4">TBD</typeparam>
    /// <typeparam name="T5">TBD</typeparam>
    /// <typeparam name="T6">TBD</typeparam>
    /// <typeparam name="T7">TBD</typeparam>
    /// <typeparam name="T8">TBD</typeparam>
    /// <typeparam name="T9">TBD</typeparam>
    /// <typeparam name="T10">TBD</typeparam>
    /// <typeparam name="T11">TBD</typeparam>
    /// <typeparam name="T12">TBD</typeparam>
    /// <typeparam name="T13">TBD</typeparam>
    internal sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> : IPartialHandlerArgumentsCapture<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        /// <param name="arguments">TBD</param>
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            _handler = (Func<object, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, bool>)handler;
            _1 = (T1)arguments[0];
            _2 = (T2)arguments[1];
            _3 = (T3)arguments[2];
            _4 = (T4)arguments[3];
            _5 = (T5)arguments[4];
            _6 = (T6)arguments[5];
            _7 = (T7)arguments[6];
            _8 = (T8)arguments[7];
            _9 = (T9)arguments[8];
            _10 = (T10)arguments[9];
            _11 = (T11)arguments[10];
            _12 = (T12)arguments[11];
            _13 = (T13)arguments[12];
        }
        private Func<object, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, bool> _handler;
        private T1 _1;
        private T2 _2;
        private T3 _3;
        private T4 _4;
        private T5 _5;
        private T6 _6;
        private T7 _7;
        private T8 _8;
        private T9 _9;
        private T10 _10;
        private T11 _11;
        private T12 _12;
        private T13 _13;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public bool Handle(T value) { return _handler(value, _1, _2, _3, _4, _5, _6, _7, _8, _9, _10, _11, _12, _13); }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    /// <typeparam name="T4">TBD</typeparam>
    /// <typeparam name="T5">TBD</typeparam>
    /// <typeparam name="T6">TBD</typeparam>
    /// <typeparam name="T7">TBD</typeparam>
    /// <typeparam name="T8">TBD</typeparam>
    /// <typeparam name="T9">TBD</typeparam>
    /// <typeparam name="T10">TBD</typeparam>
    /// <typeparam name="T11">TBD</typeparam>
    /// <typeparam name="T12">TBD</typeparam>
    /// <typeparam name="T13">TBD</typeparam>
    /// <typeparam name="T14">TBD</typeparam>
    internal sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> : IPartialHandlerArgumentsCapture<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        /// <param name="arguments">TBD</param>
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            _handler = (Func<object, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, bool>)handler;
            _1 = (T1)arguments[0];
            _2 = (T2)arguments[1];
            _3 = (T3)arguments[2];
            _4 = (T4)arguments[3];
            _5 = (T5)arguments[4];
            _6 = (T6)arguments[5];
            _7 = (T7)arguments[6];
            _8 = (T8)arguments[7];
            _9 = (T9)arguments[8];
            _10 = (T10)arguments[9];
            _11 = (T11)arguments[10];
            _12 = (T12)arguments[11];
            _13 = (T13)arguments[12];
            _14 = (T14)arguments[13];
        }
        private Func<object, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, bool> _handler;
        private T1 _1;
        private T2 _2;
        private T3 _3;
        private T4 _4;
        private T5 _5;
        private T6 _6;
        private T7 _7;
        private T8 _8;
        private T9 _9;
        private T10 _10;
        private T11 _11;
        private T12 _12;
        private T13 _13;
        private T14 _14;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public bool Handle(T value) { return _handler(value, _1, _2, _3, _4, _5, _6, _7, _8, _9, _10, _11, _12, _13, _14); }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    /// <typeparam name="T4">TBD</typeparam>
    /// <typeparam name="T5">TBD</typeparam>
    /// <typeparam name="T6">TBD</typeparam>
    /// <typeparam name="T7">TBD</typeparam>
    /// <typeparam name="T8">TBD</typeparam>
    /// <typeparam name="T9">TBD</typeparam>
    /// <typeparam name="T10">TBD</typeparam>
    /// <typeparam name="T11">TBD</typeparam>
    /// <typeparam name="T12">TBD</typeparam>
    /// <typeparam name="T13">TBD</typeparam>
    /// <typeparam name="T14">TBD</typeparam>
    /// <typeparam name="T15">TBD</typeparam>
    internal sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> : IPartialHandlerArgumentsCapture<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        /// <param name="arguments">TBD</param>
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            _handler = (Func<object, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, bool>)handler;
            _1 = (T1)arguments[0];
            _2 = (T2)arguments[1];
            _3 = (T3)arguments[2];
            _4 = (T4)arguments[3];
            _5 = (T5)arguments[4];
            _6 = (T6)arguments[5];
            _7 = (T7)arguments[6];
            _8 = (T8)arguments[7];
            _9 = (T9)arguments[8];
            _10 = (T10)arguments[9];
            _11 = (T11)arguments[10];
            _12 = (T12)arguments[11];
            _13 = (T13)arguments[12];
            _14 = (T14)arguments[13];
            _15 = (T15)arguments[14];
        }
        private Func<object, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, bool> _handler;
        private T1 _1;
        private T2 _2;
        private T3 _3;
        private T4 _4;
        private T5 _5;
        private T6 _6;
        private T7 _7;
        private T8 _8;
        private T9 _9;
        private T10 _10;
        private T11 _11;
        private T12 _12;
        private T13 _13;
        private T14 _14;
        private T15 _15;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public bool Handle(T value) { return _handler(value, _1, _2, _3, _4, _5, _6, _7, _8, _9, _10, _11, _12, _13, _14, _15); }
    }
}
