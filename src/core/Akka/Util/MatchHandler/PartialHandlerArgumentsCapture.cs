//-----------------------------------------------------------------------
// <copyright file="PartialHandlerArgumentsCapture.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Akka.Tools.MatchHandler
{
    //public interface IPartialHandler<in T>
    //{
    //    bool Handle(T value);
    //}

    public interface IPartialHandlerArgumentsCapture<T>
    {
        void Initialize(Delegate handler, IReadOnlyList<object> arguments);
        bool Handle(T message);
    }
    public sealed class PartialHandlerArgumentsCapture<T> : IPartialHandlerArgumentsCapture<T>
    {
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            //CheckParameters(handler, arguments, 1);
            _handler = (Func<object, bool>)handler;
        }

        private Func<object, bool> _handler;
        public bool Handle(T value) { return _handler(value); }
    }
    public sealed class PartialHandlerArgumentsCapture<T, T1> : IPartialHandlerArgumentsCapture<T>
    {
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            //CheckParameters(handler, arguments, 1);
            _handler = (Func<object, T1, bool>)handler;
            _1 = (T1)arguments[0];
        }

        private Func<object, T1, bool> _handler;
        private T1 _1;
        public bool Handle(T value) { return _handler(value, _1); }
    }
    public sealed class PartialHandlerArgumentsCapture<T, T1, T2> : IPartialHandlerArgumentsCapture<T>
    {
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            //CheckParameters(handler, arguments, 2);
            _handler = (Func<object, T1, T2, bool>)handler;
            _1 = (T1)arguments[0];
            _2 = (T2)arguments[1];
        }
        private Func<object, T1, T2, bool> _handler;
        private T1 _1;
        private T2 _2;
        public bool Handle(T value) { return _handler(value, _1, _2); }
    }
    public sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3> : IPartialHandlerArgumentsCapture<T>
    {
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            //CheckParameters(handler, arguments, 3);
            _handler = (Func<object, T1, T2, T3, bool>)handler;
            _1 = (T1)arguments[0];
            _2 = (T2)arguments[1];
            _3 = (T3)arguments[2];
        }
        private Func<object, T1, T2, T3, bool> _handler;
        private T1 _1;
        private T2 _2;
        private T3 _3;
        public bool Handle(T value) { return _handler(value, _1, _2, _3); }
    }
    public sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4> : IPartialHandlerArgumentsCapture<T>
    {
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            //CheckParameters(handler, arguments, 4);
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
        public bool Handle(T value) { return _handler(value, _1, _2, _3, _4); }
    }
    public sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4, T5> : IPartialHandlerArgumentsCapture<T>
    {
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            //CheckParameters(handler, arguments, 5);
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
        public bool Handle(T value) { return _handler(value, _1, _2, _3, _4, _5); }
    }
    public sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4, T5, T6> : IPartialHandlerArgumentsCapture<T>
    {
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            //CheckParameters(handler, arguments, 6);
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
        public bool Handle(T value) { return _handler(value, _1, _2, _3, _4, _5, _6); }
    }
    public sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4, T5, T6, T7> : IPartialHandlerArgumentsCapture<T>
    {
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            //CheckParameters(handler, arguments, 7);
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
        public bool Handle(T value) { return _handler(value, _1, _2, _3, _4, _5, _6, _7); }
    }
    public sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4, T5, T6, T7, T8> : IPartialHandlerArgumentsCapture<T>
    {
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            //CheckParameters(handler, arguments, 8);
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
        public bool Handle(T value) { return _handler(value, _1, _2, _3, _4, _5, _6, _7, _8); }
    }
    public sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4, T5, T6, T7, T8, T9> : IPartialHandlerArgumentsCapture<T>
    {
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            //CheckParameters(handler, arguments, 9);
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
        public bool Handle(T value) { return _handler(value, _1, _2, _3, _4, _5, _6, _7, _8, _9); }
    }
    public sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : IPartialHandlerArgumentsCapture<T>
    {
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            //CheckParameters(handler, arguments, 10);
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
        public bool Handle(T value) { return _handler(value, _1, _2, _3, _4, _5, _6, _7, _8, _9, _10); }
    }
    public sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> : IPartialHandlerArgumentsCapture<T>
    {
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            //CheckParameters(handler, arguments, 11);
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
        public bool Handle(T value) { return _handler(value, _1, _2, _3, _4, _5, _6, _7, _8, _9, _10, _11); }
    }
    public sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> : IPartialHandlerArgumentsCapture<T>
    {
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            //CheckParameters(handler, arguments, 12);
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
        public bool Handle(T value) { return _handler(value, _1, _2, _3, _4, _5, _6, _7, _8, _9, _10, _11, _12); }
    }
    public sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> : IPartialHandlerArgumentsCapture<T>
    {
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            //CheckParameters(handler, arguments, 13);
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
        public bool Handle(T value) { return _handler(value, _1, _2, _3, _4, _5, _6, _7, _8, _9, _10, _11, _12, _13); }
    }
    public sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> : IPartialHandlerArgumentsCapture<T>
    {
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            //CheckParameters(handler, arguments, 14);
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
        public bool Handle(T value) { return _handler(value, _1, _2, _3, _4, _5, _6, _7, _8, _9, _10, _11, _12, _13, _14); }
    }
    public sealed class PartialHandlerArgumentsCapture<T, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> : IPartialHandlerArgumentsCapture<T>
    {
        public void Initialize(Delegate handler, IReadOnlyList<object> arguments)
        {
            //CheckParameters(handler, arguments, 15);
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
        public bool Handle(T message) { return _handler(message, _1, _2, _3, _4, _5, _6, _7, _8, _9, _10, _11, _12, _13, _14, _15); }
    }

}

