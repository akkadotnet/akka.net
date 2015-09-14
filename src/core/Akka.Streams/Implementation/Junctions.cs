using System;
using Akka.Streams.Dsl;
using Akka.Util.Internal.Collections;

namespace Akka.Streams.Implementation
{
    public static class Junctions
    {
        public abstract class JunctionModule : Module
        {
            public override IImmutableSet<IModule> SubModules { get { return ImmutableTreeSet<IModule>.Empty; } }
            public override IModule ReplaceShape(Shape shape)
            {
                if (shape.GetType() == Shape.GetType()) return this;
                else throw new NotSupportedException("Cannot change a shape of a " + GetType());
            }
        }

        public abstract class FanInModule : JunctionModule { }
        public abstract class FanOutModule : JunctionModule { }

        public sealed class MergeModule<T> : FanInModule
        {
            private readonly UniformFanInShape<T, T> _shape;
            private readonly Attributes _attributes;

            public MergeModule(UniformFanInShape<T, T> shape, Attributes attributes = null)
            {
                if (shape == null) throw new ArgumentNullException("shape");

                _shape = shape;
                _attributes = attributes ?? Attributes.CreateName("merge");
            }

            public override Shape Shape { get { return _shape; } }

            public override IModule CarbonCopy()
            {
                return new MergeModule<T>(_shape, Attributes);
            }

            public override Attributes Attributes { get { return _attributes; } }

            public override IModule WithAttributes(Attributes attributes)
            {
                return new MergeModule<T>(_shape, attributes);
            }
        }

        public sealed class BroadcastModule<T> : FanOutModule
        {
            private readonly UniformFanOutShape<T, T> _shape;
            private readonly bool _eagerCancel;
            private readonly Attributes _attributes;

            public BroadcastModule(UniformFanOutShape<T, T> shape, bool eagerCancel, Attributes attributes)
            {
                if (shape == null) throw new ArgumentNullException("shape");

                _shape = shape;
                _eagerCancel = eagerCancel;
                _attributes = attributes;
            }

            public override Shape Shape { get { return _shape; } }

            public override IModule CarbonCopy()
            {
                return new BroadcastModule<T>(_shape.DeepCopy() as UniformFanOutShape<T, T>, _eagerCancel, Attributes);
            }

            public override Attributes Attributes { get { return _attributes; } }

            public override IModule WithAttributes(Attributes attributes)
            {
                return new BroadcastModule<T>(_shape.DeepCopy() as UniformFanOutShape<T, T>, _eagerCancel, attributes);
            }
        }

        public sealed class MergePreferredModule<T> : FanInModule
        {
            public MergePreferredModule(UniformFanInShape<T, T> shape, Attributes attributes)
            {
                throw new NotImplementedException();
            }

            public override Shape Shape
            {
                get { throw new NotImplementedException(); }
            }

            public override IModule CarbonCopy()
            {
                throw new NotImplementedException();
            }

            public override Attributes Attributes
            {
                get { throw new NotImplementedException(); }
            }

            public override IModule WithAttributes(Attributes attributes)
            {
                throw new NotImplementedException();
            }
        }

        public sealed class FlexiMergeModule<TIn, TShape> : FanInModule where TShape : Shape
        {
            public FlexiMergeModule(Shape shape, FlexiMerge.MergeLogic<TIn> mergeLogic, Attributes attributes)
            {
                throw new NotImplementedException();
            }

            public override Shape Shape
            {
                get { throw new NotImplementedException(); }
            }

            public override IModule CarbonCopy()
            {
                throw new NotImplementedException();
            }

            public override Attributes Attributes
            {
                get { throw new NotImplementedException(); }
            }

            public override IModule WithAttributes(Attributes attributes)
            {
                throw new NotImplementedException();
            }
        }

        public sealed class FlexiRouteModule<TIn, TShape> : FanOutModule where TShape : Shape
        {
            public override Shape Shape
            {
                get { throw new NotImplementedException(); }
            }

            public override IModule CarbonCopy()
            {
                throw new NotImplementedException();
            }

            public override Attributes Attributes
            {
                get { throw new NotImplementedException(); }
            }

            public override IModule WithAttributes(Attributes attributes)
            {
                throw new NotImplementedException();
            }
        }

        public sealed class BalanceModule<T> : FanOutModule
        {
            public BalanceModule(UniformFanOutShape<T, T> shape, bool waitForAllDownstreams, Attributes createName)
            {
                throw new NotImplementedException();
            }

            public override Shape Shape
            {
                get { throw new NotImplementedException(); }
            }

            public override IModule CarbonCopy()
            {
                throw new NotImplementedException();
            }

            public override Attributes Attributes
            {
                get { throw new NotImplementedException(); }
            }

            public override IModule WithAttributes(Attributes attributes)
            {
                throw new NotImplementedException();
            }
        }

        public sealed class UnzipModule<T1, T2> : FanOutModule
        {
            public override Shape Shape
            {
                get { throw new NotImplementedException(); }
            }

            public override IModule CarbonCopy()
            {
                throw new NotImplementedException();
            }

            public override Attributes Attributes
            {
                get { throw new NotImplementedException(); }
            }

            public override IModule WithAttributes(Attributes attributes)
            {
                throw new NotImplementedException();
            }
        }

        public sealed class ConcatModule<T> : FanInModule
        {
            public ConcatModule(UniformFanInShape<T, T> shape, Attributes attributes)
            {
                throw new NotImplementedException();
            }

            public override Shape Shape
            {
                get { throw new NotImplementedException(); }
            }

            public override IModule CarbonCopy()
            {
                throw new NotImplementedException();
            }

            public override Attributes Attributes
            {
                get { throw new NotImplementedException(); }
            }

            public override IModule WithAttributes(Attributes attributes)
            {
                throw new NotImplementedException();
            }
        }
    }
}