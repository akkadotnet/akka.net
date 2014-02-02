using System;
using System.Collections.Generic;
using System.Linq;

namespace Pigeon.Configuration.Hocon
{
    public class Parser
    {
        public static HoconValue Parse(string text)
        {
            return new Parser().ParseText(text);
        }

        private HoconTokenizer reader;
        private HoconValue ParseText(string text)
        {
            var root = new HoconValue();
            reader = new HoconTokenizer(text);
            reader.PullWhitespaceAndComments();
            ParseObject( root,true);
            return root;
        }

        private void ParseObject( HoconValue owner,bool root)
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

            var currentObject = owner.GetObject();

            while (!reader.EoF)
            {
                Token t = reader.PullNext();
                switch (t.Type)
                {
                    case TokenType.EoF:
                        break;
                    case TokenType.Key:
                        var value = currentObject.GetOrCreateKey(t.Value.ToString());
                        ParseKeyContent( value);
                        if (!root)
                            return;
                        break;
                    
                    case TokenType.ObjectEnd:
                        reader.PullWhitespace();
                        if (reader.IsComma())
                        {
                            reader.PullComma();
                        }
                        return;
                }
            }
        }

        private void ParseKeyContent(HoconValue value)
        {
            while (!reader.EoF)
            {
                Token t = reader.PullNext();
                switch (t.Type)
                {
                    case TokenType.Dot:
                        ParseObject(value,false);
                        return; 
                    case TokenType.Assign:
                        ParseValue(reader, value);
                        return;
                    case TokenType.ObjectStart:
                        ParseObject( value,true);
                        return;
                }
            }            
        }

        public void ParseValue(HoconTokenizer reader, HoconValue owner)
        {
            if (reader.EoF)
                throw new Exception("End of file reached while trying to read a value");

            Token t = reader.PullNextValue();
            switch (t.Type)
            {
                case TokenType.EoF:
                    break;
                case TokenType.LiteralValue:
                    ParseSimpleValue(owner, t.Value);
                    break;
                case TokenType.ObjectStart:
                    ParseObject(owner, true);
                    break;
                case TokenType.ArrayStart:
                    owner.NewValue(ParseArray());
                    break;
                case TokenType.Substitute:
                    owner.NewValue(ParseSubstitution((string)t.Value));
                    break;
            }
            reader.PullSpaceOrTab(); 
            //look for trailing values to concatenate
            if (reader.IsSubstitutionStart())
            {
                var s = reader.PullSubstitution();
                owner.AppendValue(ParseSubstitution((string)s.Value));
            }
            if (reader.IsArrayStart())
            {
                reader.PullArrayStart();               
                owner.AppendValue(ParseArray());
            }
            IgnoreComma();
        }

        private static HoconSubstitution ParseSubstitution(string value )
        {
            return new HoconSubstitution(value);
        }

        public HoconArray ParseArray()
        {
            var arr = new HoconArray();
            ParseArrayContents(arr);
            return arr;
        }

        private void ParseArrayContents(HoconArray arr)
        {
            while (!reader.EoF)
            {
                Token t = reader.PullNextValue();
                switch (t.Type)
                {
                    case TokenType.EoF:
                        break;
                    case TokenType.LiteralValue:
                        {
                            var value = new HoconValue();
                            arr.Add(value);
                            ParseSimpleValue(value, t.Value);
                            IgnoreComma();
                            break;
                        }
                    case TokenType.ObjectStart:
                        {
                            var value = new HoconValue();
                            arr.Add(value);
                            ParseObject(value, true);
                            break;
                        }
                    case TokenType.ArrayStart:
                        {
                            var value = new HoconValue();
                            value.NewValue(ParseArray());
                            arr.Add(value);
                            break;
                        }
                    case TokenType.ArrayEnd:
                        {
                            reader.PullWhitespace();
                            IgnoreComma();

                            return;
                        }
                }
            }
            throw new Exception("End of file reached when parsing array");
        }

        private void ParseSimpleValue(HoconValue v, object value)
        {
            v.NewValue(value);
            while (reader.IsStartSimpleValue()) //fetch rest of values if string concat
            {
                var t = reader.PullSimpleValue();
                v.AppendValue(t.Value);
            }
        }

        private void IgnoreComma()
        {
            if (reader.IsComma()) //optional end of value
            {
                reader.PullComma();
            }
        }
    }
}