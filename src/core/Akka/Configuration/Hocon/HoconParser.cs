//-----------------------------------------------------------------------
// <copyright file="HoconParser.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Configuration.Hocon
{
    /// <summary>
    /// This class contains methods used to parse HOCON (Human-Optimized Config Object Notation)
    /// configuration strings.
    /// </summary>
    public class Parser
    {
        private readonly List<HoconSubstitution> _substitutions = new List<HoconSubstitution>();
        private HoconTokenizer _reader;
        private HoconValue _root;
        private Func<string, HoconRoot> _includeCallback;

        /// <summary>
        /// Parses the supplied HOCON configuration string into a root element.
        /// </summary>
        /// <param name="text">The string that contains a HOCON configuration string.</param>
        /// <param name="includeCallback">Callback used to resolve includes</param>
        /// <exception cref="FormatException">This exception is thrown if an unresolved substitution or an unknown token is encountered.</exception>
        /// <exception cref="Exception">This exception is thrown if the end of the file has been reached while trying to read a value.</exception>
        /// <returns>The root element created from the supplied HOCON configuration string.</returns>
        public static HoconRoot Parse(string text,Func<string,HoconRoot> includeCallback)
        {
            return new Parser().ParseText(text,includeCallback);
        }

        private HoconRoot ParseText(string text,Func<string,HoconRoot> includeCallback)
        {
            _includeCallback = includeCallback;
            _root = new HoconValue();
            _reader = new HoconTokenizer(text);
            _reader.PullWhitespaceAndComments();
            ParseObject(_root, true,"");

            var c = new Config(new HoconRoot(_root, Enumerable.Empty<HoconSubstitution>()));
            foreach (HoconSubstitution sub in _substitutions)
            {
                HoconValue res = c.GetValue(sub.Path);
                if (res == null)
                    throw new FormatException($"Unresolved substitution: {sub.Path}");
                sub.ResolvedValue = res;
            }
            return new HoconRoot(_root, _substitutions);
        }

        private void ParseObject(HoconValue owner, bool root,string currentPath)
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

            while (!_reader.EoF)
            {
                Token t = _reader.PullNext();
                switch (t.Type)
                {
                    case TokenType.Include:
                        var included = _includeCallback(t.Value);
                        var substitutions = included.Substitutions;
                        foreach (var substitution in substitutions)
                        {
                            //fixup the substitution, add the current path as a prefix to the substitution path
                            substitution.Path = currentPath + "." + substitution.Path;
                        }
                        _substitutions.AddRange(substitutions);
                        var otherObj = included.Value.GetObject();
                        owner.GetObject().Merge(otherObj);

                        break;
                    case TokenType.EoF:
                        break;
                    case TokenType.Key:
                        HoconValue value = currentObject.GetOrCreateKey(t.Value);
                        var nextPath = currentPath == "" ? t.Value : currentPath + "." + t.Value;
                        ParseKeyContent(value, nextPath);
                        if (!root)
                            return;
                        break;

                    case TokenType.ObjectEnd:
                        return;
                }
            }
        }

        private void ParseKeyContent(HoconValue value,string currentPath)
        {
            while (!_reader.EoF)
            {
                Token t = _reader.PullNext();
                switch (t.Type)
                {
                    case TokenType.Dot:
                        ParseObject(value, false,currentPath);
                        return;
                    case TokenType.Assign:
                        
                        if (!value.IsObject())
                        {
                            //if not an object, then replace the value.
                            //if object. value should be merged
                            value.Clear();
                        }
                        ParseValue(value,currentPath);
                        return;
                    case TokenType.ObjectStart:
                        ParseObject(value, true,currentPath);
                        return;
                }
            }
        }

        /// <summary>
        /// Retrieves the next value token from the tokenizer and appends it
        /// to the supplied element <paramref name="owner"/>.
        /// </summary>
        /// <param name="owner">The element to append the next token.</param>
        /// <param name="currentPath">The location in the HOCON object hierarchy that the parser is currently reading.</param>
        /// <exception cref="Exception">This exception is thrown if the end of the file has been reached while trying to read a value.</exception>
        public void ParseValue(HoconValue owner, string currentPath)
        {
            if (_reader.EoF)
                throw new Exception("End of file reached while trying to read a value");

            _reader.PullWhitespaceAndComments();
            while (_reader.IsValue())
            {
                Token t = _reader.PullValue();

                switch (t.Type)
                {
                    case TokenType.EoF:
                        break;
                    case TokenType.LiteralValue:
                        if (owner.IsObject())
                        {
                            //needed to allow for override objects
                            owner.Clear();
                        }
                        var lit = new HoconLiteral
                        {
                            Value = t.Value
                        };
                        owner.AppendValue(lit);

                        break;
                    case TokenType.ObjectStart:
                        ParseObject(owner, true,currentPath);
                        break;
                    case TokenType.ArrayStart:
                        HoconArray arr = ParseArray(currentPath);
                        owner.AppendValue(arr);
                        break;
                    case TokenType.Substitute:
                        HoconSubstitution sub = ParseSubstitution(t.Value);
                        _substitutions.Add(sub);
                        owner.AppendValue(sub);
                        break;
                }
                if (_reader.IsSpaceOrTab())
                {
                    ParseTrailingWhitespace(owner);
                }
            }

            IgnoreComma();
        }

        private void ParseTrailingWhitespace(HoconValue owner)
        {
            Token ws = _reader.PullSpaceOrTab();
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

        private static HoconSubstitution ParseSubstitution(string value)
        {
            return new HoconSubstitution(value);
        }

        /// <summary>
        /// Retrieves the next array token from the tokenizer.
        /// </summary>
        /// <param name="currentPath">The location in the HOCON object hierarchy that the parser is currently reading.</param>
        /// <returns>An array of elements retrieved from the token.</returns>
        public HoconArray ParseArray(string currentPath)
        {
            var arr = new HoconArray();
            while (!_reader.EoF && !_reader.IsArrayEnd())
            {
                var v = new HoconValue();
                ParseValue(v,currentPath);
                arr.Add(v);
                _reader.PullWhitespaceAndComments();
            }
            _reader.PullArrayEnd();
            return arr;
        }

        private void IgnoreComma()
        {
            if (_reader.IsComma()) //optional end of value
            {
                _reader.PullComma();
            }
        }
    }
}

