using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Akka.MultiNode.TestAdapter.Configuration
{
    class JsonArray : JsonValue
    {
        readonly JsonValue[] _array;

        public JsonArray(JsonValue[] array, int line, int column)
            : base(line, column)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            _array = array;
        }

        public int Length
        {
            get { return _array.Length; }
        }

        public JsonValue this[int index]
        {
            get { return _array[index]; }
        }
    }

    class JsonBoolean : JsonValue
    {
        public JsonBoolean(JsonToken token)
            : base(token.Line, token.Column)
        {
            if (token.Type == JsonTokenType.True)
            {
                Value = true;
            }
            else if (token.Type == JsonTokenType.False)
            {
                Value = false;
            }
            else
            {
                throw new ArgumentException("Token value should be either True or False.", nameof(token));
            }
        }

        public bool Value { get; private set; }

        public static implicit operator bool (JsonBoolean jsonBoolean)
        {
            return jsonBoolean.Value;
        }
    }

    class JsonBuffer
    {
        public const string ValueNull = "null";
        public const string ValueTrue = "true";
        public const string ValueFalse = "false";

        StringBuilder _buffer = new StringBuilder();
        StringBuilder _codePointBuffer = new StringBuilder(4);
        readonly TextReader _reader;
        JsonToken _token;
        int _line;
        int _column;

        public JsonBuffer(TextReader reader)
        {
            _reader = reader;
            _line = 1;
        }

        public JsonToken Read()
        {
            int first;
            while (true)
            {
                first = ReadNextChar();

                if (first == -1)
                {
                    _token.Type = JsonTokenType.EOF;
                    return _token;
                }
                else if (!IsWhitespace(first))
                {
                    break;
                }
            }

            _token.Value = ((char)first).ToString();
            _token.Line = _line;
            _token.Column = _column;

            if (first == '{')
            {
                _token.Type = JsonTokenType.LeftCurlyBracket;
            }
            else if (first == '}')
            {
                _token.Type = JsonTokenType.RightCurlyBracket;
            }
            else if (first == '[')
            {
                _token.Type = JsonTokenType.LeftSquareBracket;
            }
            else if (first == ']')
            {
                _token.Type = JsonTokenType.RightSquareBracket;
            }
            else if (first == ':')
            {
                _token.Type = JsonTokenType.Colon;
            }
            else if (first == ',')
            {
                _token.Type = JsonTokenType.Comma;
            }
            else if (first == '"')
            {
                _token.Type = JsonTokenType.String;
                _token.Value = ReadString();
            }
            else if (first == 't')
            {
                ReadLiteral(ValueTrue);
                _token.Type = JsonTokenType.True;
            }
            else if (first == 'f')
            {
                ReadLiteral(ValueFalse);
                _token.Type = JsonTokenType.False;
            }
            else if (first == 'n')
            {
                ReadLiteral(ValueNull);
                _token.Type = JsonTokenType.Null;
            }
            else if ((first >= '0' && first <= '9') || first == '-')
            {
                _token.Type = JsonTokenType.Number;
                _token.Value = ReadNumber(first);
            }
            else
            {
                throw new JsonDeserializerException(
                    JsonDeserializerResource.Format_IllegalCharacter(first),
                    _token);
            }

            // JsonToken is a value type
            return _token;
        }

        int ReadNextChar()
        {
            while (true)
            {
                var value = _reader.Read();
                _column++;
                switch (value)
                {
                    case -1:
                        // This is the end of file
                        return -1;
                    case '\n':
                        // This is a new line. Let the next loop read the first character of the following line.
                        // Set position ahead of next line
                        _column = 0;
                        _line++;

                        continue;
                    case '\r':
                        break;
                    default:
                        // Returns the normal value
                        return value;
                }
            }
        }

        string ReadNumber(int firstRead)
        {
#if NET35
            _buffer = new StringBuilder();
#else
            _buffer.Clear();
#endif
            _buffer.Append((char)firstRead);

            while (true)
            {
                var next = _reader.Peek();

                if ((next >= '0' && next <= '9') ||
                    next == '.' ||
                    next == 'e' ||
                    next == 'E')
                {
                    _buffer.Append((char)ReadNextChar());
                }
                else
                {
                    break;
                }
            }

            return _buffer.ToString();
        }

        void ReadLiteral(string literal)
        {
            for (int i = 1; i < literal.Length; ++i)
            {
                var next = _reader.Peek();
                if (next != literal[i])
                {
                    throw new JsonDeserializerException(
                        JsonDeserializerResource.Format_UnrecognizedLiteral(literal),
                        _line, _column);
                }
                else
                {
                    ReadNextChar();
                }
            }

            var tail = _reader.Peek();
            if (tail != '}' &&
                tail != ']' &&
                tail != ',' &&
                tail != '\n' &&
                tail != -1 &&
                !IsWhitespace(tail))
            {
                throw new JsonDeserializerException(
                    JsonDeserializerResource.Format_IllegalTrailingCharacterAfterLiteral(tail, literal),
                    _line, _column);
            }
        }

        string ReadString()
        {
#if NET35
            _buffer = new StringBuilder();
#else
            _buffer.Clear();
#endif
            var escaped = false;

            while (true)
            {
                var next = ReadNextChar();

                if (next == -1 || next == '\n')
                {
                    throw new JsonDeserializerException(
                        JsonDeserializerResource.JSON_OpenString,
                        _line, _column);
                }
                else if (escaped)
                {
                    if ((next == '"') || (next == '\\') || (next == '/'))
                    {
                        _buffer.Append((char)next);
                    }
                    else if (next == 'b')
                    {
                        // '\b' backspace
                        _buffer.Append('\b');
                    }
                    else if (next == 'f')
                    {
                        // '\f' form feed
                        _buffer.Append('\f');
                    }
                    else if (next == 'n')
                    {
                        // '\n' line feed
                        _buffer.Append('\n');
                    }
                    else if (next == 'r')
                    {
                        // '\r' carriage return
                        _buffer.Append('\r');
                    }
                    else if (next == 't')
                    {
                        // '\t' tab
                        _buffer.Append('\t');
                    }
                    else if (next == 'u')
                    {
                        // '\uXXXX' unicode
                        var unicodeLine = _line;
                        var unicodeColumn = _column;

#if NET35
                        _codePointBuffer = new StringBuilder(4);
#else
                        _codePointBuffer.Clear();
#endif
                        for (int i = 0; i < 4; ++i)
                        {
                            next = ReadNextChar();
                            if (next == -1)
                            {
                                throw new JsonDeserializerException(
                                    JsonDeserializerResource.JSON_InvalidEnd,
                                    unicodeLine,
                                    unicodeColumn);
                            }
                            else
                            {
                                _codePointBuffer[i] = (char)next;
                            }
                        }

                        try
                        {
                            var unicodeValue = int.Parse(_codePointBuffer.ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                            _buffer.Append((char)unicodeValue);
                        }
                        catch (FormatException ex)
                        {
                            throw new JsonDeserializerException(
                                JsonDeserializerResource.Format_InvalidUnicode(_codePointBuffer.ToString()),
                                ex,
                                unicodeLine,
                                unicodeColumn);
                        }
                    }
                    else
                    {
                        throw new JsonDeserializerException(
                            JsonDeserializerResource.Format_InvalidSyntaxNotExpected("character escape", "\\" + next),
                            _line,
                            _column);
                    }

                    escaped = false;
                }
                else if (next == '\\')
                {
                    escaped = true;
                }
                else if (next == '"')
                {
                    break;
                }
                else
                {
                    _buffer.Append((char)next);
                }
            }

            return _buffer.ToString();
        }

        static bool IsWhitespace(int value)
        {
            return value == ' ' || value == '\t' || value == '\r';
        }
    }

    static class JsonDeserializer
    {
        public static JsonValue Deserialize(TextReader reader)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            var buffer = new JsonBuffer(reader);

            var result = DeserializeInternal(buffer.Read(), buffer);

            // There are still unprocessed char. The parsing is not finished. Error happened.
            var nextToken = buffer.Read();
            if (nextToken.Type != JsonTokenType.EOF)
            {
                throw new JsonDeserializerException(
                    JsonDeserializerResource.Format_UnfinishedJSON(nextToken.Value),
                    nextToken);
            }

            return result;
        }

        static JsonValue DeserializeInternal(JsonToken next, JsonBuffer buffer)
        {
            if (next.Type == JsonTokenType.EOF)
            {
                return null;
            }

            if (next.Type == JsonTokenType.LeftSquareBracket)
            {
                return DeserializeArray(next, buffer);
            }

            if (next.Type == JsonTokenType.LeftCurlyBracket)
            {
                return DeserializeObject(next, buffer);
            }

            if (next.Type == JsonTokenType.String)
            {
                return new JsonString(next.Value, next.Line, next.Column);
            }

            if (next.Type == JsonTokenType.True || next.Type == JsonTokenType.False)
            {
                return new JsonBoolean(next);
            }

            if (next.Type == JsonTokenType.Null)
            {
                return new JsonNull(next.Line, next.Column);
            }

            if (next.Type == JsonTokenType.Number)
            {
                return new JsonNumber(next);
            }

            throw new JsonDeserializerException(JsonDeserializerResource.Format_InvalidTokenExpectation(
                next.Value, "'{', '[', true, false, null, JSON string, JSON number, or the end of the file"),
                next);
        }

        static JsonArray DeserializeArray(JsonToken head, JsonBuffer buffer)
        {
            var list = new List<JsonValue>();
            while (true)
            {
                var next = buffer.Read();
                if (next.Type == JsonTokenType.RightSquareBracket)
                {
                    break;
                }

                list.Add(DeserializeInternal(next, buffer));

                next = buffer.Read();
                if (next.Type == JsonTokenType.EOF)
                {
                    throw new JsonDeserializerException(
                        JsonDeserializerResource.Format_InvalidSyntaxExpectation("JSON array", ']', ','),
                        next);
                }
                else if (next.Type == JsonTokenType.RightSquareBracket)
                {
                    break;
                }
                else if (next.Type != JsonTokenType.Comma)
                {
                    throw new JsonDeserializerException(
                        JsonDeserializerResource.Format_InvalidSyntaxExpectation("JSON array", ','),
                        next);
                }
            }

            return new JsonArray(list.ToArray(), head.Line, head.Column);
        }

        static JsonObject DeserializeObject(JsonToken head, JsonBuffer buffer)
        {
            var dictionary = new Dictionary<string, JsonValue>();

            // Loop through each JSON entry in the input object
            while (true)
            {
                var next = buffer.Read();
                if (next.Type == JsonTokenType.EOF)
                {
                    throw new JsonDeserializerException(
                        JsonDeserializerResource.Format_InvalidSyntaxExpectation("JSON object", '}'),
                        next);
                }

                if (next.Type == JsonTokenType.Colon)
                {
                    throw new JsonDeserializerException(
                        JsonDeserializerResource.Format_InvalidSyntaxNotExpected("JSON object", ':'),
                        next);
                }
                else if (next.Type == JsonTokenType.RightCurlyBracket)
                {
                    break;
                }
                else
                {
                    if (next.Type != JsonTokenType.String)
                    {
                        throw new JsonDeserializerException(
                            JsonDeserializerResource.Format_InvalidSyntaxExpectation("JSON object member name", "JSON string"),
                            next);
                    }

                    var memberName = next.Value;
                    if (dictionary.ContainsKey(memberName))
                    {
                        throw new JsonDeserializerException(
                            JsonDeserializerResource.Format_DuplicateObjectMemberName(memberName),
                            next);
                    }

                    next = buffer.Read();
                    if (next.Type != JsonTokenType.Colon)
                    {
                        throw new JsonDeserializerException(
                            JsonDeserializerResource.Format_InvalidSyntaxExpectation("JSON object", ':'),
                            next);
                    }

                    dictionary[memberName] = DeserializeInternal(buffer.Read(), buffer);

                    next = buffer.Read();
                    if (next.Type == JsonTokenType.RightCurlyBracket)
                    {
                        break;
                    }
                    else if (next.Type != JsonTokenType.Comma)
                    {
                        throw new JsonDeserializerException(
                            JsonDeserializerResource.Format_InvalidSyntaxExpectation("JSON object", ',', '}'),
                            next);
                    }
                }
            }

            return new JsonObject(dictionary, head.Line, head.Column);
        }
    }

    class JsonDeserializerException : Exception
    {
        public JsonDeserializerException(string message, Exception innerException, int line, int column)
            : base(message, innerException)
        {
            Line = line;
            Column = column;
        }

        public JsonDeserializerException(string message, int line, int column)
            : base(message)
        {
            Line = line;
            Column = column;
        }

        public JsonDeserializerException(string message, JsonToken nextToken)
            : base(message)
        {
            Line = nextToken.Line;
            Column = nextToken.Column;
        }

        public int Line { get; }

        public int Column { get; }
    }

    static class JsonDeserializerResource
    {
        internal static string Format_IllegalCharacter(int value)
        {
            return $"Illegal character '{(char)value}' (Unicode hexadecimal {value:X4}).";
        }

        internal static string Format_IllegalTrailingCharacterAfterLiteral(int value, string literal)
        {
            return $"Illegal character '{(char)value}' (Unicode hexadecimal {value:X4}) after the literal name '{literal}'.";
        }

        internal static string Format_UnrecognizedLiteral(string literal)
        {
            return $"Invalid JSON literal. Expected literal '{literal}'.";
        }

        internal static string Format_DuplicateObjectMemberName(string memberName)
        {
            return Format_InvalidSyntax("JSON object", $"Duplicate member name '{memberName}'");
        }

        internal static string Format_InvalidFloatNumberFormat(string raw)
        {
            return $"Invalid float number format: {raw}";
        }

        internal static string Format_FloatNumberOverflow(string raw)
        {
            return $"Float number overflow: {raw}";
        }

        internal static string Format_InvalidSyntax(string syntaxName, string issue)
        {
            return $"Invalid {syntaxName} syntax. {issue}.";
        }

        internal static string Format_InvalidSyntaxNotExpected(string syntaxName, char unexpected)
        {
            return $"Invalid {syntaxName} syntax. Unexpected '{unexpected}'.";
        }

        internal static string Format_InvalidSyntaxNotExpected(string syntaxName, string unexpected)
        {
            return $"Invalid {syntaxName} syntax. Unexpected {unexpected}.";
        }

        internal static string Format_InvalidSyntaxExpectation(string syntaxName, char expectation)
        {
            return $"Invalid {syntaxName} syntax. Expected '{expectation}'.";
        }

        internal static string Format_InvalidSyntaxExpectation(string syntaxName, string expectation)
        {
            return $"Invalid {syntaxName} syntax. Expected {expectation}.";
        }

        internal static string Format_InvalidSyntaxExpectation(string syntaxName, char expectation1, char expectation2)
        {
            return $"Invalid {syntaxName} syntax. Expected '{expectation1}' or '{expectation2}'.";
        }

        internal static string Format_InvalidTokenExpectation(string tokenValue, string expectation)
        {
            return $"Unexpected token '{tokenValue}'. Expected {expectation}.";
        }

        internal static string Format_InvalidUnicode(string unicode)
        {
            return $"Invalid Unicode [{unicode}]";
        }

        internal static string Format_UnfinishedJSON(string nextTokenValue)
        {
            return $"Invalid JSON end. Unprocessed token {nextTokenValue}.";
        }

        internal static string JSON_OpenString
        {
            get { return Format_InvalidSyntaxExpectation("JSON string", '\"'); }
        }

        internal static string JSON_InvalidEnd
        {
            get { return "Invalid JSON. Unexpected end of file."; }
        }
    }

    class JsonNull : JsonValue
    {
        public JsonNull(int line, int column)
            : base(line, column)
        {
        }
    }

    class JsonNumber : JsonValue
    {
        readonly string _raw;
        readonly double _double;

        public JsonNumber(JsonToken token)
            : base(token.Line, token.Column)
        {
            try
            {
                _raw = token.Value;
                _double = double.Parse(_raw, NumberStyles.Float);
            }
            catch (FormatException ex)
            {
                throw new JsonDeserializerException(
                    JsonDeserializerResource.Format_InvalidFloatNumberFormat(_raw),
                    ex,
                    token.Line,
                    token.Column);
            }
            catch (OverflowException ex)
            {
                throw new JsonDeserializerException(
                    JsonDeserializerResource.Format_FloatNumberOverflow(_raw),
                    ex,
                    token.Line,
                    token.Column);
            }
        }

        public double Double
        {
            get { return _double; }
        }

        public string Raw
        {
            get { return _raw; }
        }
    }

    class JsonObject : JsonValue
    {
        readonly IDictionary<string, JsonValue> _data;

        public JsonObject(IDictionary<string, JsonValue> data, int line, int column)
            : base(line, column)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            _data = data;
        }

        public ICollection<string> Keys
        {
            get { return _data.Keys; }
        }

        public JsonValue Value(string key)
        {
            JsonValue result;
            if (!_data.TryGetValue(key, out result))
            {
                result = null;
            }

            return result;
        }

        public JsonObject ValueAsJsonObject(string key)
        {
            return Value(key) as JsonObject;
        }

        public JsonString ValueAsString(string key)
        {
            return Value(key) as JsonString;
        }

        public int ValueAsInt(string key)
        {
            var number = Value(key) as JsonNumber;
            if (number == null)
            {
                throw new FormatException();
            }
            return Convert.ToInt32(number.Raw);
        }

        public bool ValueAsBoolean(string key, bool defaultValue = false)
        {
            var boolVal = Value(key) as JsonBoolean;
            if (boolVal != null)
            {
                return boolVal.Value;
            }

            return defaultValue;
        }

        public bool? ValueAsNullableBoolean(string key)
        {
            var boolVal = Value(key) as JsonBoolean;
            if (boolVal != null)
            {
                return boolVal.Value;
            }

            return null;
        }

        public string[] ValueAsStringArray(string key)
        {
            var list = Value(key) as JsonArray;
            if (list == null)
            {
                return null;
            }

            var result = new string[list.Length];

            for (int i = 0; i < list.Length; ++i)
            {
                var jsonString = list[i] as JsonString;
                result[i] = jsonString?.ToString();
            }

            return result;
        }
    }

    class JsonString : JsonValue
    {
        readonly string _value;

        public JsonString(string value, int line, int column)
            : base(line, column)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            _value = value;
        }

        public string Value
        {
            get { return _value; }
        }

        public override string ToString()
        {
            return _value;
        }

        public static implicit operator string (JsonString instance)
        {
            if (instance == null)
            {
                return null;
            }
            else
            {
                return instance.Value;
            }
        }
    }

    struct JsonToken
    {
        public JsonTokenType Type;
        public string Value;
        public int Line;
        public int Column;
    }

    enum JsonTokenType
    {
        LeftCurlyBracket,   // [
        LeftSquareBracket,  // {
        RightCurlyBracket,  // ]
        RightSquareBracket, // }
        Colon,              // :
        Comma,              // ,
        Null,
        True,
        False,
        Number,
        String,
        EOF
    }

    class JsonValue
    {
        public JsonValue(int line, int column)
        {
            Line = line;
            Column = column;
        }

        public int Line { get; }

        public int Column { get; }
    }
}
