using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Configuration.Hocon
{
    /// <summary>
    /// Class Parser.
    /// </summary>
    public class Parser
    {
        /// <summary>
        /// The substitutions
        /// </summary>
        private readonly List<HoconSubstitution> substitutions = new List<HoconSubstitution>();
        /// <summary>
        /// The reader
        /// </summary>
        private HoconTokenizer reader;
        /// <summary>
        /// The root
        /// </summary>
        private HoconValue root;

        /// <summary>
        /// Parses the specified text.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>HoconValue.</returns>
        public static HoconRoot Parse(string text)
        {
            return new Parser().ParseText(text);
        }

        /// <summary>
        /// Parses the text.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>HoconValue.</returns>
        /// <exception cref="System.Exception">Unresolved substitution: + sub.Path</exception>
        private HoconRoot ParseText(string text)
        {
            root = new HoconValue();
            reader = new HoconTokenizer(text);
            reader.PullWhitespaceAndComments();
            ParseObject(root, true);

            var c = new Config(new HoconRoot(root,Enumerable.Empty<HoconSubstitution>()));
            foreach (HoconSubstitution sub in substitutions)
            {
                HoconValue res = c.GetValue(sub.Path);
                if (res == null)
                    throw new Exception("Unresolved substitution:" + sub.Path);
                sub.ResolvedValue = res;
            }
            return new HoconRoot(root, substitutions);
        }

        /// <summary>
        /// Parses the object.
        /// </summary>
        /// <param name="owner">The owner.</param>
        /// <param name="root">if set to <c>true</c> [root].</param>
        private void ParseObject(HoconValue owner, bool root)
        {
            if (owner.IsObject())
            {
                //the value of this KVP is already an object
            }
            else
            {
                //the value of this KVP is not an object, thus, we should add a new
                owner.NewValue(new HoconObject());
            }

            HoconObject currentObject = owner.GetObject();

            while (!reader.EoF)
            {
                Token t = reader.PullNext();
                switch (t.Type)
                {
                    case TokenType.EoF:
                        break;
                    case TokenType.Key:
                        HoconValue value = currentObject.GetOrCreateKey(t.Value);
                        ParseKeyContent(value);
                        if (!root)
                            return;
                        break;

                    case TokenType.ObjectEnd:
                        return;
                }
            }
        }

        /// <summary>
        /// Parses the content of the key.
        /// </summary>
        /// <param name="value">The value.</param>
        private void ParseKeyContent(HoconValue value)
        {
            while (!reader.EoF)
            {
                Token t = reader.PullNext();
                switch (t.Type)
                {
                    case TokenType.Dot:
                        ParseObject(value, false);
                        return;
                    case TokenType.Assign:
                        ParseValue(value);
                        return;
                    case TokenType.ObjectStart:
                        ParseObject(value, true);
                        return;
                }
            }
        }

        /// <summary>
        /// Parses the value.
        /// </summary>
        /// <param name="owner">The owner.</param>
        /// <exception cref="System.Exception">End of file reached while trying to read a value</exception>
        public void ParseValue(HoconValue owner)
        {
            if (reader.EoF)
                throw new Exception("End of file reached while trying to read a value");

            bool isObject = owner.IsObject();
            reader.PullWhitespaceAndComments();
            while (reader.IsValue())
            {
                Token t = reader.PullValue();

                switch (t.Type)
                {
                    case TokenType.EoF:
                        break;
                    case TokenType.LiteralValue:
                        if (isObject)
                        {
                            //needed to allow for override objects
                            isObject = false;
                            owner.Clear();
                        }
                        var lit = new HoconLiteral
                        {
                            Value = t.Value
                        };
                        owner.AppendValue(lit);

                        break;
                    case TokenType.ObjectStart:
                        ParseObject(owner, true);
                        break;
                    case TokenType.ArrayStart:
                        HoconArray arr = ParseArray();
                        owner.AppendValue(arr);
                        break;
                    case TokenType.Substitute:
                        HoconSubstitution sub = ParseSubstitution(t.Value);
                        substitutions.Add(sub);
                        owner.AppendValue(sub);
                        break;
                }
                if (reader.IsSpaceOrTab())
                {
                    ParseTrailingWhitespace(owner);
                }
            }

            IgnoreComma();
        }

        /// <summary>
        /// Parses the trailing whitespace.
        /// </summary>
        /// <param name="owner">The owner.</param>
        private void ParseTrailingWhitespace(HoconValue owner)
        {
            Token ws = reader.PullSpaceOrTab();
            //single line ws should be included if string concat
            if (ws.Value.Length > 0)
            {
                var wsLit = new HoconLiteral
                {
                    Value = ws.Value,
                };
                owner.AppendValue(wsLit);
            }
        }

        /// <summary>
        /// Parses the substitution.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>HoconSubstitution.</returns>
        private static HoconSubstitution ParseSubstitution(string value)
        {
            return new HoconSubstitution(value);
        }

        /// <summary>
        /// Parses the array.
        /// </summary>
        /// <returns>HoconArray.</returns>
        public HoconArray ParseArray()
        {
            var arr = new HoconArray();
            while (!reader.EoF && !reader.IsArrayEnd())
            {
                var v = new HoconValue();
                ParseValue(v);
                arr.Add(v);
                reader.PullWhitespaceAndComments();
            }
            reader.PullArrayEnd();
            return arr;
        }

        /// <summary>
        /// Ignores the comma.
        /// </summary>
        private void IgnoreComma()
        {
            if (reader.IsComma()) //optional end of value
            {
                reader.PullComma();
            }
        }
    }
}