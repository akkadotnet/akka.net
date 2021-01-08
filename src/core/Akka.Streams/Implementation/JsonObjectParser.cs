//-----------------------------------------------------------------------
// <copyright file="JsonObjectParser.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Annotations;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.Util;
using Akka.Util;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// INTERNAL API: Use <see cref="JsonFraming"/> instead
    /// 
    /// **Mutable** framing implementation that given any number of <see cref="ByteString"/> chunks, can emit JSON objects contained within them.
    /// Typically JSON objects are separated by new-lines or commas, however a top-level JSON Array can also be understood and chunked up
    /// into valid JSON objects by this framing implementation.
    /// 
    /// Leading whitespace between elements will be trimmed.
    /// </summary>
    [InternalApi]
    public class JsonObjectParser
    {
        private static readonly byte SquareBraceStart = Convert.ToByte('[');
        private static readonly byte SquareBraceEnd = Convert.ToByte(']');
        private static readonly byte CurlyBraceStart = Convert.ToByte('{');
        private static readonly byte CurlyBraceEnd = Convert.ToByte('}');
        private static readonly byte DoubleQuote = Convert.ToByte('"');
        private static readonly byte Backslash = Convert.ToByte('\\');
        private static readonly byte Comma = Convert.ToByte(',');

        private static readonly byte LineBreak = Convert.ToByte('\n');
        private static readonly byte LineBreak2 = Convert.ToByte('\r');
        private static readonly byte Tab = Convert.ToByte('\t');
        private static readonly byte Space = Convert.ToByte(' ');

        private static readonly byte[] Whitespace = {LineBreak, LineBreak2, Tab, Space};

        private static bool IsWhitespace(byte input) => Whitespace.Contains(input);

        private readonly int _maximumObjectLength;
        private ByteString _buffer = ByteString.Empty;
        private int _pos; // latest position of pointer while scanning for json object end
        private int _trimFront;
        private int _depth;
        private int _charsInObject;
        private bool _completedObject;
        private bool _inStringExpression;
        private bool _isStartOfEscapeSequence;
        private byte _lastInput = 0;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="maximumObjectLength">TBD</param>
        public JsonObjectParser(int maximumObjectLength = int.MaxValue)
        {
            _maximumObjectLength = maximumObjectLength;
        }

        private bool OutsideObject => _depth == 0;

        private bool InsideObject => !OutsideObject;

        /// <summary>
        /// Appends input ByteString to internal byte string buffer.
        /// Use <see cref="Poll"/> to extract contained JSON objects.
        /// </summary>
        /// <param name="input">TBD</param>
        public void Offer(ByteString input) => _buffer += input;

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsEmpty => _buffer.IsEmpty;

        /// <summary>
        /// Attempt to locate next complete JSON object in buffered <see cref="ByteString"/> and returns it if found.
        /// May throw a <see cref="Framing.FramingException"/> if the contained JSON is invalid or max object size is exceeded.
        /// </summary>
        /// <exception cref="Framing.FramingException">TBD</exception>
        /// <returns>TBD</returns>
        public Option<ByteString> Poll()
        {
            var foundObject = SeekObject();
            if(!foundObject || _pos == -1 || _pos == 0)
                return Option<ByteString>.None;

            var emit = _buffer.Slice(0, _pos);
            var buffer = _buffer.Slice(_pos);
            _buffer = buffer.Compact();
            _pos = 0;

            var trimFront = _trimFront;
            _trimFront = 0;

            if (trimFront == 0)
                return emit;

            var trimmed = emit.Slice(trimFront);
            return trimmed.IsEmpty ? Option<ByteString>.None : trimmed;
        }

        /// <summary>
        /// Returns true if an entire valid JSON object was found, false otherwise
        /// </summary>
        private bool SeekObject()
        {
            _completedObject = false;
            var bufferSize = _buffer.Count;
            while (_pos != -1 && (_pos < bufferSize && _pos < _maximumObjectLength) && !_completedObject)
                Proceed(_buffer[_pos]);

            if (_pos >= _maximumObjectLength)
                throw new Framing.FramingException(
                    $"JSON element exceeded maximumObjectLength ({_maximumObjectLength} bytes)!");

            return _completedObject;
        }

        private void Proceed(byte input)
        {
            if (input == SquareBraceStart && OutsideObject)
            {
                // outer object is an array
                _pos++;
                _trimFront++;
            }
            else if (input == SquareBraceEnd && OutsideObject)
                // outer array completed!
                _pos = -1;
            else if (input == Comma && OutsideObject)
            {
                // do nothing
                _pos++;
                _trimFront++;
            }
            else if (input == Backslash)
            {
                _isStartOfEscapeSequence = _lastInput != Backslash;
                _pos++;
            }
            else if (input == DoubleQuote)
            {
                if (!_isStartOfEscapeSequence)
                    _inStringExpression = !_inStringExpression;
                _isStartOfEscapeSequence = false;
                _pos++;
            }
            else if (input == CurlyBraceStart && !_inStringExpression)
            {
                _isStartOfEscapeSequence = false;
                _depth++;
                _pos++;
            }
            else if (input == CurlyBraceEnd && !_inStringExpression)
            {
                _isStartOfEscapeSequence = false;
                _depth--;
                _pos++;
                if (_depth == 0)
                {
                    _charsInObject = 0;
                    _completedObject = true;
                }
            }
            else if (IsWhitespace(input) && !_inStringExpression)
            {
                _pos++;
                if (_depth == 0)
                    _trimFront++;
            }
            else if (InsideObject)
            {
                _isStartOfEscapeSequence = false;
                _pos++;
            }
            else
                throw new Framing.FramingException($"Invalid JSON encountered at position {_pos} of {_buffer}");

            _lastInput = input;
        }
    }
}
