// //-----------------------------------------------------------------------
// // <copyright file="CompressionUtils.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.IO;
using System.IO.Compression;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;

namespace Akka.Streams.Implementation.IO.Compression
{
    public interface ICompressor
    {
        ByteString Compress(ByteString data);
        
        ByteString Flush();
        ByteString Finish();
        ByteString CompressAndFlush(ByteString input);
        ByteString CompressAndFinish(ByteString input);
        void Close();
    }

    
    public class DeflateCompressor : ICompressor
    {
        private CompressionMode _mode;
        private MemoryStream _ms = new MemoryStream();
        public DeflateCompressor(CompressionMode mode)
        {
            _mode = mode;
            
        }
        public ByteString Compress(ByteString data)
        {
            try
            {
                _ms.Position = 0;
                //using (var _ms = new MemoryStream())
                {
                    if (_mode == CompressionMode.Compress)
                    {
                 

                            using (var _stream =
                                new DeflateStream(_ms, CompressionLevel.Fastest,true))
                            {
                                data.WriteTo(_stream);
                            }
                    }
                    else
                    {
                        using (var inflate = new MemoryStream())
                        {
                            data.WriteTo(inflate);
                            inflate.Position = 0;
                            using (var _stream = new DeflateStream(inflate, _mode,true))
                        {
                            _stream.CopyTo(_ms);
                        }
                        }
                    }
                    return ByteString.FromBytes(_ms.ToArray(),0,(int)_ms.Position);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        protected ByteString compressWithBuffer(ByteString data, byte[] buffer)
        {
            using (var ms = new MemoryStream(buffer))
            {
                using (var _stream =
                    new DeflateStream(new MemoryStream(buffer), _mode))
                {
                    data.WriteTo(_stream);
                }   
                return ByteString.FromBytes(buffer,0,(int)ms.Length);
            }
            
        }
        

        public ByteString Flush()
        {
            return ByteString.Empty;
        }

        public ByteString Finish()
        {
            return ByteString.Empty;
        }

        public ByteString CompressAndFlush(ByteString input)
        {
            return Compress(input);
        }

        public ByteString CompressAndFinish(ByteString input)
        {
            throw new NotImplementedException();
        }

        public void Close()
        {
            throw new NotImplementedException();
        }
    }
    public class CompressorFlow : SimpleLinearGraphStage<ByteString>
    {
        public CompressorFlow(Func<ICompressor> newCompressor)
        {
            _newCompressor = newCompressor;
        }

        Func<ICompressor> _newCompressor;
        private sealed class Logic : InAndOutGraphStageLogic, IInHandler, IOutHandler
        {
            private readonly CompressorFlow _stage;
            private readonly Decider _decider;
            private ICompressor _compressor;
            public Logic(CompressorFlow stage,ICompressor compressor, Attributes inheritedAttributes) : base(stage.Shape)
            {
                _compressor = compressor;
                _stage = stage;
                var attr = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                _decider = attr != null ? attr.Decider : Deciders.StoppingDecider;
                SetHandler(stage.Inlet, stage.Outlet, this);
            }

            public override void OnPush()
            {
                try
                {
                    var buf = _compressor.CompressAndFlush(Grab(_stage.Inlet));
                    if (buf.Count > 0)
                    {
                        Push(_stage.Outlet,buf);
                    }
                    else
                    {
                        Pull(_stage.Inlet);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    if (_decider(ex) == Directive.Stop)
                        FailStage(ex);
                    else
                        Pull(_stage.Inlet);
                }
            }

            public void OnUpstreamFinish()
            {
                var buf = _compressor.Finish();
                if (buf.Count > 0)
                {
                    Emit(_stage.Outlet,buf);
                }
            }

            public void OnUpstreamFailure(Exception e)
            {
                
            }

            public override void OnPull() => Pull(_stage.Inlet);
            public void OnDownstreamFinish()
            {
                
            }
        }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return  new Logic(this,_newCompressor(),inheritedAttributes);
        }
    }
    
}