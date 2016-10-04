//-----------------------------------------------------------------------
// <copyright file="JsonFraming.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.IO;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Provides JSON framing stages that can separate valid JSON objects from incoming <see cref="ByteString"/> objects.
    /// </summary>
    public static class JsonFraming
    {
        /// <summary>
        /// Returns a Flow that implements a "brace counting" based framing stage for emitting valid JSON chunks.
        /// It scans the incoming data stream for valid JSON objects and returns chunks of ByteStrings containing only those valid chunks.
        /// 
        /// Typical examples of data that one may want to frame using this stage include:
        /// 
        /// <para>
        /// **Very large arrays**:
        /// {{{
        ///     [{"id": 1}, {"id": 2}, [...], {"id": 999}]
        /// }}}
        /// </para>
        ///  
        /// <para>
        /// **Multiple concatenated JSON objects** (with, or without commas between them):
        /// {{{
        ///     {"id": 1}, {"id": 2}, [...], {"id": 999}
        /// }}}
        /// </para>
        /// 
        /// The framing works independently of formatting, i.e. it will still emit valid JSON elements even if two
        /// elements are separated by multiple newlines or other whitespace characters. And of course is insensitive
        /// (and does not impact the emitting frame) to the JSON object's internal formatting.
        /// 
        /// Framing raw JSON values (such as integers or strings) is supported as well.
        /// </summary>
        /// <param name="maximumObjectLength">The maximum length of allowed frames while decoding. If the maximum length is exceeded this Flow will fail the stream.</param>
        /// <returns></returns>
        public static Flow<ByteString, ByteString, NotUsed> ObjectScanner(int maximumObjectLength)
        {
            return Flow.Create<ByteString>().Via(new Scanner(maximumObjectLength));
        }

        private sealed class Scanner : SimpleLinearGraphStage<ByteString>
        {
            private sealed class Logic : GraphStageLogic
            {
                private readonly Scanner _stage;
                private readonly JsonObjectParser _buffer;

                public Logic(Scanner stage) : base(stage.Shape)
                {
                    _stage = stage;
                    _buffer = new JsonObjectParser(stage._maximumObjectLength);

                    SetHandler(stage.Outlet, onPull: TryPopBuffer);

                    SetHandler(stage.Inlet, onPush: () =>
                    {
                        _buffer.Offer(Grab(stage.Inlet));
                        TryPopBuffer();
                    }, onUpstreamFinish: () =>
                    {
                        var json = _buffer.Poll();
                        if (json.HasValue)
                            Emit(stage.Outlet, json.Value, CompleteStage);
                        else
                            CompleteStage();
                    });
                }

                private void TryPopBuffer()
                {
                    try
                    {
                        var json = _buffer.Poll();
                        if (json.HasValue)
                            Push(_stage.Outlet, json.Value);
                        else if(IsClosed(_stage.Inlet))
                            CompleteStage();
                        else
                            Pull(_stage.Inlet);
                            
                    }
                    catch(Exception ex)
                    {
                        FailStage(ex);
                    }
                }
            }

            private readonly int _maximumObjectLength;

            public Scanner(int maximumObjectLength)
            {
                _maximumObjectLength = maximumObjectLength;
            }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }
    }
}
