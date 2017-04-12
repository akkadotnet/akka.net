using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Util.Internal;

namespace Akka.Actor
{
    public abstract class BTBase : UntypedActor
    {
        public class WFTimeout
        {
            public Guid Id { get; set; }
        }
    }

    /// <summary>
    /// Behavior Tree Actor
    /// </summary>
    /// <typeparam name="TData">Global data object accessible to all scopes.</typeparam>
    public class BT<TData> : BTBase, IMachineHost
    {
        protected TreeMachine Machine { get; set; }

        protected override void OnReceive(object message)
        {
            if (!Machine.ProcessMessage(message))
                Unhandled(message);
        }

        protected TreeMachine.ActionWF Execute(Action<TreeMachine.IContext> action)
            => new TreeMachine.ActionWF(action);

        protected TreeMachine.IfWF If(TreeMachine.IWorkflow cond, TreeMachine.IWorkflow @then = null, TreeMachine.IWorkflow @else = null)
            => new TreeMachine.IfWF(cond, @then, @else);

        protected TreeMachine.IfWF If(Func<TreeMachine.IContext, bool> pred, TreeMachine.IWorkflow @then = null, TreeMachine.IWorkflow @else = null)
            => new TreeMachine.IfWF(Condition(pred), @then, @else);

        protected TreeMachine.ConditionWF Condition(Func<TreeMachine.IContext, bool> pred)
            => new TreeMachine.ConditionWF(pred);

        protected TreeMachine.ReceiveAnyWF ReceiveAny(TreeMachine.IWorkflow child)
            => new TreeMachine.ReceiveAnyWF(child);

        protected TreeMachine.ReceiveAnyWF ReceiveAny(Func<object, bool> shouldHandle, TreeMachine.IWorkflow child)
            => new TreeMachine.ReceiveAnyWF(shouldHandle, child);

        protected TreeMachine.ReceiveWF<T> Receive<T>(TreeMachine.IWorkflow child)
            => new TreeMachine.ReceiveWF<T>(child);

        protected TreeMachine.ReceiveWF<T> Receive<T>(Func<T, bool> shouldHandle, TreeMachine.IWorkflow child)
            => new TreeMachine.ReceiveWF<T>(shouldHandle, child);

        protected TreeMachine.SelectorWF AnySucceed(params TreeMachine.IWorkflow[] children)
            => new TreeMachine.SelectorWF(children);

        protected TreeMachine.SequenceWF AllSucceed(params TreeMachine.IWorkflow[] children)
            => new TreeMachine.SequenceWF(children);

        protected TreeMachine.AllCompleteWF AllComplete(params TreeMachine.IWorkflow[] children)
            => new TreeMachine.AllCompleteWF(children);

        protected TreeMachine.LoopWF Loop(TreeMachine.IWorkflow child)
            => new TreeMachine.LoopWF(child);

        protected TreeMachine.ForeverWF Forever(TreeMachine.IWorkflow child)
            => new TreeMachine.ForeverWF(child);

        protected TreeMachine.WhileWF While(Func<TreeMachine.IContext, bool> pred, TreeMachine.IWorkflow child)
            => new TreeMachine.WhileWF(pred, child);

        protected TreeMachine.IWorkflow While(TreeMachine.IWorkflow cond, TreeMachine.IWorkflow child)
            => Pass(Loop(If(cond, Pass(child), Fail())));

        protected TreeMachine.NotWF Not(TreeMachine.IWorkflow child)
            => new TreeMachine.NotWF(child);

        protected TreeMachine.PassWF Pass(TreeMachine.IWorkflow child)
            => new TreeMachine.PassWF(child);

        protected TreeMachine.FailWF Fail()
            => new TreeMachine.FailWF();

        protected TreeMachine.AfterWF After(TreeMachine.IWorkflow child, TreeMachine.IWorkflow after)
            => new TreeMachine.AfterWF(child, after);

        protected TreeMachine.ParallelWF Parallel(Func<IEnumerable<WorkflowStatus>, WorkflowStatus> statusEval, params TreeMachine.IWorkflow[] children)
            => new TreeMachine.ParallelWF(statusEval, children);

        protected TreeMachine.BecomeWF Become(Func<TreeMachine.IWorkflow> factory)
            => new TreeMachine.BecomeWF(factory);

        protected TreeMachine.SpawnWF Spawn(TreeMachine.IWorkflow child)
            => new TreeMachine.SpawnWF(child);

        protected TreeMachine.TimeoutWF Timeout(TimeSpan delay, TreeMachine.IWorkflow child, TreeMachine.IWorkflow onTimeout = null)
            => new TreeMachine.TimeoutWF(_ => delay, child, onTimeout ?? Fail());

        protected TreeMachine.TimeoutWF Timeout(Func<TreeMachine.IContext, TimeSpan> delay, TreeMachine.IWorkflow child, TreeMachine.IWorkflow onTimeout = null)
            => new TreeMachine.TimeoutWF(delay, child, onTimeout ?? Fail());

        protected TreeMachine.NeverWF Never()
            => new TreeMachine.NeverWF();

        protected TreeMachine.IWorkflow Delay(TimeSpan delay, TreeMachine.IWorkflow after = null)
            => Delay(_ => delay, after ?? Ok());

        protected TreeMachine.IWorkflow Delay(Func<TreeMachine.IContext, TimeSpan> delay, TreeMachine.IWorkflow after = null)
            => new TreeMachine.TimeoutWF(delay, Never(), after ?? Ok());

        protected TreeMachine.OkWF Ok()
            => new TreeMachine.OkWF();

        protected void StartWith(TreeMachine.IWorkflow wf, TData data)
        {
            Machine = new TreeMachine(data, null, this);
            Machine.Run(wf);
        }

        public IDisposable ScheduleMessage(TimeSpan delay, object message)
        {
            return new CancellableOnDispose(
                Context.System.Scheduler.ScheduleTellOnceCancelable(delay, Self, message, Sender));
        }

        private class CancellableOnDispose : IDisposable
        {
            private bool _disposedValue = false; // To detect redundant calls

            public CancellableOnDispose(ICancelable cancel)
            {
                Cancel = cancel;
            }

            public ICancelable Cancel { get; }

            public void Dispose()
            {
                if (!_disposedValue)
                {
                    if (!Cancel.IsCancellationRequested)
                    {
                        try
                        {
                            Cancel.Cancel(false);
                        }
                        catch { }
                    }

                    try
                    {
                        var toDispose = Cancel as IDisposable;
                        toDispose?.Dispose();
                    }
                    catch { }

                    _disposedValue = true;
                }
            }
        }

        public class TreeMachine
        {
            private List<ScopeWF> _scopes = new List<ScopeWF>();

            private IBlackboard _rootBb;

            public TreeMachine(TData data, IBlackboard rootBb, IMachineHost host)
            {
                Data = data;
                _rootBb = rootBb;
                Host = host;
            }

            public IMachineHost Host { get; }

            public bool ProcessMessage(object message)
            {
                return _scopes.Select(s => s.ProcessMessage(message)).ToList().Any(b => b);
            }

            public void Run(IWorkflow wf)
            {
                _scopes.Add(new ScopeWF(wf));
                Run();
            }

            public void Run()
            {
                _scopes.ForEach(s => s.Run(new WFContext(Data, null, s, Host)));
            }

            protected class WFContext : IContext
            {
                public WFContext(TData data, object message, ScopeWF root, IMachineHost host)
                {
                    GlobalData = data;
                    CurrentMessage = message;
                    Root = root;
                    Host = host;
                }

                public object CurrentMessage { get; set; }

                public TData GlobalData { get; }

                public ScopeWF Root { get; }

                public IBlackboard ScopeData
                {
                    get
                    {
                        throw new NotImplementedException();
                    }
                }

                public IMachineHost Host { get; }
            }
            public TData Data { get; }

            public abstract class WFBase : IWorkflow
            {
                public virtual WorkflowStatus Status { get; protected set; }

                public object Result { get; protected set; }

                public abstract void Run(IContext context);

                public bool IsCompleted => Status == WorkflowStatus.Success || Status == WorkflowStatus.Failure;

                public abstract void Reset();
            }

            public class ScopeWF : WFBase, ITransmit
            {
                private IWorkflow _current;
                private Stack<IWorkflow> _stack = new Stack<IWorkflow>();
                private Stack<IContext> _contexts = new Stack<IContext>();
                private ScopeWF _root;

                private IContext _context;

                public ScopeWF(IWorkflow child, ScopeWF root = null)
                {
                    _current = child;
                    Status = _current.Status;
                    _root = root ?? this;
                }

                public IWorkflow Child => _current;

                public bool RunAgain =>
                    _current != null && !(_current as ITransmit)?.RunAgain == false;

                public override void Reset()
                {

                }

                public void Become(Func<IWorkflow> factory, IContext context)
                {
                    _stack.Clear();
                    _contexts.Clear();

                    if (_root != this)
                    {
                        _current = null;
                        Status = WorkflowStatus.Success;
                        _root.Become(factory, context);
                    }
                    else
                    {
                        _current = factory();
                        Run(context);
                    }
                }

                public override void Run(IContext context)
                {
                    if (IsCompleted)
                        return;

                    _context = new WFContext(context.GlobalData, context.CurrentMessage, _root, context.Host);

                    bool done = false;
                    do
                    {
                        Status = _current.Status;

                        if (IsCompleted)
                        {
                            if (_stack.Count > 0)
                            {
                                if (_current is IReceive)
                                {
                                    _context = _contexts.Pop();
                                }
                                _current = _stack.Pop();
                            }
                            else
                            {
                                done = true;
                            }
                        }
                        else
                        {
                            var becomer = _current as IBecome;
                            if (becomer != null)
                            {
                                Become(becomer.Factory, _context);
                                return;
                            }

                            if (!(_current is IReceive))
                            {
                                _current.Run(_context);

                                var decorator = _current as IDecorator;
                                var transmitter = _current as ITransmit;

                                if (decorator != null)
                                {
                                    if (!decorator.Status.IsCompleted())
                                    {
                                        _stack.Push(decorator);

                                        _current = decorator.Next();
                                    }
                                }
                                else if (transmitter?.RunAgain == false && transmitter.Status.IsCompleted() == false)
                                {
                                    done = true;
                                }
                            }
                            else
                            {
                                done = true;
                            }
                        }
                    } while (!done);
                }

                public bool ProcessMessage(object message)
                {
                    var prev = _context;
                    _context = new WFContext(_context.GlobalData, message, _root, _context.Host);

                    if (IsCompleted)
                        return false;

                    var processor = _current as IProcessMessage;

                    if (processor != null)
                    {
                        if (processor.ProcessMessage(message))
                        {
                            if (!IsCompleted)
                            {
                                var receiver = processor as IReceive;

                                if (receiver != null)
                                {
                                    _contexts.Push(prev);
                                    _stack.Push(_current);
                                    _current = receiver.Child;
                                }

                                Run(_context);
                            }

                            return true;
                        }
                    }

                    return false;
                }
            }

            public class ReceiveAnyWF : WFBase, IReceive
            {
                private bool _hasProcessed;
                private Func<object, bool> _shouldHandle;

                public ReceiveAnyWF(IWorkflow child)
                {
                    Child = child;
                }

                public ReceiveAnyWF(Func<object, bool> shouldHandle, IWorkflow child) : this(child)
                {
                    _shouldHandle = shouldHandle;
                }

                public override WorkflowStatus Status
                    => _hasProcessed ? Child?.Status ?? WorkflowStatus.Success : WorkflowStatus.Undetermined;

                public IWorkflow Child { get; }

                public override void Reset()
                {
                    Child?.Reset();
                    _hasProcessed = false;
                }

                public override void Run(IContext context)
                {
                    throw new InvalidOperationException("Receive tasks don't run.");
                }

                public virtual bool ProcessMessage(object message)
                {
                    if (message is WFTimeout)
                        return false;

                    Status = Child?.Status ?? WorkflowStatus.Success;

                    _hasProcessed = _shouldHandle == null || _shouldHandle(message);

                    return _hasProcessed;
                }
            }

            public class ReceiveWF<T> : ReceiveAnyWF
            {
                public ReceiveWF(IWorkflow child) : base(o => o is T, child)
                {
                }

                public ReceiveWF(Func<T, bool> shouldHandle, IWorkflow child)
                    : base(o => o is T && shouldHandle((T)o), child)
                {
                }
            }

            public class ActionWF : WFBase
            {
                private Action<IContext> _action;

                public ActionWF(Action<IContext> action)
                {
                    _action = action;
                }

                public override void Reset()
                {
                    Status = WorkflowStatus.Undetermined;
                }

                public override void Run(IContext context)
                {
                    try
                    {
                        Status = WorkflowStatus.Running;

                        _action(context);

                        Status = WorkflowStatus.Success;
                    }
                    catch (Exception ex)
                    {
                        Status = WorkflowStatus.Failure;
                        Result = ex;
                    }
                }
            }

            public class NeverWF : WFBase, ITransmit
            {
                public bool RunAgain => false;

                public bool ProcessMessage(object message)
                {
                    return false;
                }

                public override void Reset()
                {
                    Status = WorkflowStatus.Undetermined;
                }

                public override void Run(IContext context)
                {
                    Status = WorkflowStatus.Running;
                }
            }

            public class FailWF : WFBase
            {
                public override void Reset()
                {
                    Status = WorkflowStatus.Undetermined;
                }

                public override void Run(IContext context)
                {
                    Status = WorkflowStatus.Failure;
                }
            }

            public class OkWF : WFBase
            {
                public override void Reset()
                {
                    Status = WorkflowStatus.Undetermined;
                }

                public override void Run(IContext context)
                {
                    Status = WorkflowStatus.Success;
                }
            }

            public class IfWF : WFBase, IDecorator
            {
                private readonly IWorkflow _cond;
                private readonly IWorkflow _then;
                private readonly IWorkflow _else;
                private IWorkflow _next;
                private bool _condHasRun;

                public IfWF(IWorkflow cond, IWorkflow then = null, IWorkflow @else = null)
                {
                    _cond = cond;
                    _then = then;
                    _else = @else;
                }

                public IWorkflow Next()
                {
                    return _next;
                }

                public override void Reset()
                {
                    Status = WorkflowStatus.Undetermined;
                    _next = null;
                    _cond.Reset();
                    _then?.Reset();
                    _else?.Reset();
                    _condHasRun = false;
                }

                public override void Run(IContext context)
                {
                    if (_next == null)
                    {
                        Status = WorkflowStatus.Running;
                        _next = _cond;
                        return;
                    }

                    try
                    {
                        if (!_condHasRun)
                        {
                            _condHasRun = true;

                            var status = _cond.Status;

                            _next = status == WorkflowStatus.Success ? _then : _else;

                            Status = _next != null ? WorkflowStatus.Running : status;

                        }
                        else
                        {
                            Status = _next.Status;
                        }
                    }
                    catch (Exception ex)
                    {
                        Status = WorkflowStatus.Failure;
                        Result = ex;
                    }
                }
            }

            public class ConditionWF : WFBase
            {
                private readonly Func<IContext, bool> _pred;

                public ConditionWF(Func<IContext, bool> pred)
                {
                    _pred = pred;
                }

                public override void Reset()
                {
                    Status = WorkflowStatus.Undetermined;
                }

                public override void Run(IContext context)
                {
                    Status = _pred(context) ? WorkflowStatus.Success : WorkflowStatus.Failure;
                }
            }

            public class SelectorWF : SequentialBase
            {
                public SelectorWF(params IWorkflow[] children)
                    : base(children) { }

                public override void Run(IContext ctx)
                {
                    if (Current?.Status == WorkflowStatus.Success)
                    {
                        Status = WorkflowStatus.Success;
                    }

                    if (!IsCompleted)
                    {
                        if (CurrentPosition + 1 >= Children.Count)
                        {
                            Status = WorkflowStatus.Failure;
                        }
                        else
                        {
                            Status = WorkflowStatus.Running;
                        }
                    }
                }
            }

            public class SequenceWF : SequentialBase
            {
                public SequenceWF(params IWorkflow[] children)
                    : base(children) { }

                public override void Run(IContext ctx)
                {
                    if (Current?.Status == WorkflowStatus.Failure)
                    {
                        Status = WorkflowStatus.Failure;
                    }

                    if (!IsCompleted)
                    {
                        if (CurrentPosition + 1 >= Children.Count)
                        {
                            Status = WorkflowStatus.Success;
                        }
                        else
                        {
                            Status = WorkflowStatus.Running;
                        }
                    }
                }
            }

            public class AllCompleteWF : SequentialBase
            {
                public AllCompleteWF(params IWorkflow[] children)
                    : base(children) { }

                public override void Run(IContext ctx)
                {
                    if (CurrentPosition + 1 >= Children.Count)
                    {
                        Status = WorkflowStatus.Success;
                    }
                    else
                    {
                        Status = WorkflowStatus.Running;
                    }
                }
            }

            public class AfterWF : SequentialBase
            {
                private WorkflowStatus _childStatus;
                private IWorkflow _child;
                private IWorkflow _after;

                public AfterWF(IWorkflow child, IWorkflow after)
                    : base(child, after)
                {
                    _child = child;
                    _after = after;
                }

                public override void Run(IContext ctx)
                {
                    if (CurrentPosition <= 0)
                    {
                        _childStatus = Current?.Status ?? WorkflowStatus.Undetermined;
                        Status = WorkflowStatus.Running;
                    }
                    else
                    {
                        var status = Current.Status;
                        if (status.IsCompleted())
                        {
                            Status = _childStatus;
                        }
                    }
                }
            }

            public abstract class SequentialBase : WFBase, IDecorator
            {
                protected SequentialBase(params IWorkflow[] children)
                {
                    Children = new List<IWorkflow>(children);
                }

                protected List<IWorkflow> Children { get; }
                protected IWorkflow Current { get; set; }
                protected int CurrentPosition { get; set; } = -1;

                public override void Reset()
                {
                    CurrentPosition = -1;
                    Children.ForEach(c => c.Reset());
                    Status = WorkflowStatus.Undetermined;
                    Current = null;
                }

                public IWorkflow Next()
                {
                    if (CurrentPosition < Children.Count)
                    {
                        CurrentPosition++;

                        Current = CurrentPosition < Children.Count ? Children[CurrentPosition] : null;
                    }

                    return Current;
                }
            }

            public class LoopWF : LoopBase
            {
                public LoopWF(IWorkflow child)
                    : base(child) { }

                public override void Run(IContext context)
                {
                    if (Child?.Status == WorkflowStatus.Failure)
                    {
                        Status = WorkflowStatus.Failure;
                    }
                    else
                    {
                        Status = WorkflowStatus.Running;
                    }
                }
            }

            public class ForeverWF : LoopBase
            {
                public ForeverWF(IWorkflow child)
                    : base(child) { }

                public override void Run(IContext context)
                {
                    Status = WorkflowStatus.Running;
                }
            }

            public class NotWF : LoopBase
            {
                public NotWF(IWorkflow child)
                    : base(child) { }

                public override void Run(IContext context)
                {
                    Status = Child.Status == WorkflowStatus.Success
                        ? WorkflowStatus.Failure : Child.Status == WorkflowStatus.Failure
                            ? WorkflowStatus.Success : Child.Status;
                }
            }

            public class PassWF : LoopBase
            {
                public PassWF(IWorkflow child)
                    : base(child) { }

                public override void Run(IContext context)
                {
                    Status = Child.Status.IsCompleted() ? WorkflowStatus.Success : Child.Status;
                }
            }

            public class WhileWF : LoopBase
            {
                private Func<IContext, bool> _pred;

                public WhileWF(Func<IContext, bool> pred, IWorkflow child)
                    : base(child)
                {
                    _pred = pred;
                }

                public override void Run(IContext context)
                {
                    Status = _pred(context) ? WorkflowStatus.Running : WorkflowStatus.Success;
                }
            }

            public abstract class LoopBase : WFBase, IDecorator
            {
                protected LoopBase(IWorkflow child)
                {
                    Child = child;
                }

                protected IWorkflow Child { get; }

                public override void Reset()
                {
                    Status = WorkflowStatus.Undetermined;
                    Child?.Reset();
                }

                public IWorkflow Next()
                {
                    Child?.Reset();
                    return Child;
                }
            }

            public class TimeoutWF : WFBase, ITransmit
            {
                private IWorkflow _child;
                private Func<IContext, TimeSpan> _delay;
                private IWorkflow _onTimeout;
                private IDisposable _timeoutToken;
                private Guid _timeoutId;
                private bool _timedOut;
                private ITransmit _childScope;
                private ITransmit _onTimeoutScope;

                public TimeoutWF(Func<IContext, TimeSpan> delay, IWorkflow child, IWorkflow onTimeout)
                {
                    _delay = delay;
                    _child = child;
                    _onTimeout = onTimeout;
                }

                public bool RunAgain =>
                    !_timedOut
                        ? _childScope.RunAgain
                        : _onTimeoutScope.RunAgain;

                public bool ProcessMessage(object message)
                {
                    if (Status.IsCompleted())
                        return false;

                    var tmsg = message as WFTimeout;
                    if (tmsg?.Id == _timeoutId)
                    {
                        _timedOut = true;
                        _timeoutToken.Dispose();

                        return true;
                    }

                    if (!_timedOut)
                    {
                        return _childScope.ProcessMessage(message);
                    }

                    return _onTimeoutScope.ProcessMessage(message);
                }

                public override void Reset()
                {
                    Status = WorkflowStatus.Undetermined;
                    _child.Reset();
                    _onTimeout.Reset();
                    _childScope = null;
                    _onTimeoutScope = null;
                    _timeoutToken?.Dispose();
                    _timeoutToken = null;
                    _timedOut = false;
                }

                public override void Run(IContext context)
                {
                    if (Status.IsCompleted())
                        return;

                    var wfContext = context as WFContext;

                    if (_childScope == null)
                    {
                        _childScope = (_child as ITransmit) ?? new ScopeWF(_child, wfContext.Root);
                    }

                    if (_onTimeoutScope == null)
                    {
                        _onTimeoutScope = (_onTimeout as ITransmit) ?? new ScopeWF(_onTimeout, wfContext.Root);
                    }

                    if (_timeoutToken == null)
                    {
                        _timeoutId = Guid.NewGuid();
                        _timeoutToken = context.Host.ScheduleMessage(_delay(context), new WFTimeout { Id = _timeoutId });
                        Status = WorkflowStatus.Running;
                    }

                    if (!_timedOut)
                    {
                        _childScope.Run(context);

                        if (_childScope.Status.IsCompleted())
                        {
                            Status = _childScope.Status;
                            _timeoutToken.Dispose();
                        }
                    }
                    else
                    {
                        _onTimeoutScope.Run(context);

                        if (_onTimeoutScope.Status.IsCompleted())
                        {
                            Status = _onTimeoutScope.Status;
                        }
                    }
                }
            }

            public class ParallelWF : WFBase, ITransmit
            {
                private readonly Func<IEnumerable<WorkflowStatus>, WorkflowStatus> _statusEval;
                private List<ITransmit> _children;
                private readonly IWorkflow[] _wfChildren;

                public bool RunAgain =>
                    _children.Any(c => c.RunAgain);

                public ParallelWF(Func<IEnumerable<WorkflowStatus>, WorkflowStatus> statusEval, params IWorkflow[] children)
                {
                    _statusEval = statusEval;
                    _wfChildren = children;
                }

                public override void Reset()
                {
                    Status = WorkflowStatus.Undetermined;
                    _wfChildren.ForEach(c => c.Reset());
                    _children = null;
                }

                public override void Run(IContext context)
                {
                    if (Status.IsCompleted())
                        return;

                    var wfContext = context as WFContext;

                    if (_children == null)
                    {
                        _children = _wfChildren.Select(c => c as ITransmit ?? new ScopeWF(c, wfContext.Root)).ToList();
                    }

                    _children.ForEach(c => c.Run(context));

                    var states = _children.Select(c => c.Status).ToList();

                    var status = _statusEval(states);

                    if (!GetIsComplete(status) && (states.Count == 0 || states.All(GetIsComplete)))
                    {
                        Status = WorkflowStatus.Failure;
                    }
                    else
                    {
                        Status = status;
                    }
                }

                private static bool GetIsComplete(WorkflowStatus s)
                {
                    return s == WorkflowStatus.Failure || s == WorkflowStatus.Success;
                }

                public bool ProcessMessage(object message)
                {
                    return _children.Select(c => c.ProcessMessage(message)).ToList().Any(b => b);
                }
            }

            public class BecomeWF : WFBase, IBecome
            {
                public BecomeWF(Func<IWorkflow> factory)
                {
                    Factory = factory;
                }

                public Func<IWorkflow> Factory { get; }

                public override void Reset()
                {
                    Status = WorkflowStatus.Undetermined;
                }

                public override void Run(IContext context)
                {
                }
            }

            public class SpawnWF : WFBase, ITransmit
            {
                private IWorkflow _child;

                public SpawnWF(IWorkflow child)
                {
                    _child = child;
                }

                public ScopeWF Scope { get; private set; }

                public bool RunAgain
                    => Scope.RunAgain;

                public bool ProcessMessage(object message)
                {
                    return Scope.ProcessMessage(message);
                }

                public override void Reset()
                {
                    Status = WorkflowStatus.Undetermined;
                    _child.Reset();
                    Scope = null;
                }

                public override void Run(IContext context)
                {
                    if (Status.IsCompleted())
                        return;

                    if (Scope == null)
                    {
                        Scope = new ScopeWF(_child);
                    }

                    Scope.Run(context);

                    Status = Scope.Status;
                }
            }

            /// <summary>
            /// Blackboard allows extension. Extension emulates local scope.
            /// Extended blackboard can hide dictionary entries of previous scope blackboard.
            /// Lower scope blackboard cannot modify higher scope blackboard.
            /// </summary>
            public interface IBlackboard : IDictionary<object, object>
            {
                /// <summary>
                /// Concatenation of blackboard messages from all scopes.
                /// </summary>
                IReadOnlyList<object> Messages { get; }

                /// <summary>
                /// Prepend message.
                /// </summary>
                /// <param name="msg"></param>
                void PushMessage(object msg);

                /// <summary>
                /// Remove first message. Only local blackboard is affected.
                /// When local message list is empty, return null.
                /// </summary>
                /// <returns>Message object or null.</returns>
                object PopMessage();

                /// <summary>
                /// Extend blackboard for local modification.
                /// </summary>
                /// <returns>Extended blackboard.</returns>
                IBlackboard Extend();
                /// <summary>
                /// Discard extended blackboard.
                /// </summary>
                /// <returns>Previous blackboard.</returns>
                IBlackboard Retract();
            }

            public interface IContext
            {
                TData GlobalData { get; }

                object CurrentMessage { get; set; }

                //object CurrentItem { get; set; }

                IBlackboard ScopeData { get; }

                IMachineHost Host { get; }
            }

            public interface IWorkflow
            {
                WorkflowStatus Status { get; }

                object Result { get; }

                void Run(IContext context);

                void Reset();
            }

            public interface IAction : IWorkflow
            {
            }

            public interface IBecome : IWorkflow
            {
                Func<IWorkflow> Factory { get; }
            }

            public interface IDecorator : IWorkflow
            {
                IWorkflow Next();
            }

            public interface ISplit : ITransmit
            {
                IWorkflow[] Children { get; }
            }

            public interface ITransmit : IProcessMessage
            {
                bool RunAgain { get; }
            }

            public interface IParent : IWorkflow
            {
                IWorkflow Child { get; }
            }

            public interface ISpawn : IParent
            {
            }

            public interface IProcessMessage : IWorkflow
            {
                bool ProcessMessage(object message);
            }
            public interface IReceive : IProcessMessage, IParent
            {
            }

            public interface IReceive<T> : IReceive
            {
                bool ProcessMessage(T message);
            }
        }
    }

    public interface IMachineHost
    {
        IDisposable ScheduleMessage(TimeSpan delay, object message);
    }

    /// <summary>
    /// Workflow Progress.
    /// </summary>
    public enum WorkflowStatus
    {
        Undetermined,
        Running,
        Success,
        Failure
    }

    public static class WorkflowEx
    {
        public static bool IsCompleted(this WorkflowStatus status)
            => status == WorkflowStatus.Failure || status == WorkflowStatus.Success;

        public static WorkflowStatus AllSucceed(this IEnumerable<WorkflowStatus> states)
        {
            return GetStatus(states, CalcAllSucceed);
        }

        public static WorkflowStatus FirstSucceed(this IEnumerable<WorkflowStatus> states)
        {
            return GetStatus(states, CalcFirstSucceed);
        }

        public static WorkflowStatus AnySucceed(this IEnumerable<WorkflowStatus> states)
        {
            return GetStatus(states, CalcAnySucceed);
        }

        public static WorkflowStatus AllComplete(this IEnumerable<WorkflowStatus> states)
        {
            return GetStatus(states, CalcAllComplete);
        }

        private static WorkflowStatus GetStatus(IEnumerable<WorkflowStatus> states, CalcStatus calc)
        {
            bool hasFailure = false;
            bool hasSuccess = false;
            bool hasRunning = false;
            bool hasUnknown = false;
            bool hasAny = false;

            foreach (var s in states)
            {
                hasFailure = s == WorkflowStatus.Failure;
                hasSuccess = s == WorkflowStatus.Success;
                hasRunning = s == WorkflowStatus.Running;
                hasUnknown = s == WorkflowStatus.Undetermined;
                hasAny = true;
            }

            return calc(hasFailure, hasSuccess, hasRunning, hasUnknown, hasAny);
        }

        private static WorkflowStatus CalcAllSucceed(bool hasFailure, bool hasSuccess, bool hasRunning, bool hasUnknown, bool hasAny)
        {
            return hasFailure
                ? WorkflowStatus.Failure
                : hasRunning || hasUnknown
                    ? WorkflowStatus.Running
                    : WorkflowStatus.Success;
        }

        private static WorkflowStatus CalcFirstSucceed(bool hasFailure, bool hasSuccess, bool hasRunning, bool hasUnknown, bool hasAny)
        {
            return !hasAny || hasFailure
                ? WorkflowStatus.Failure
                : hasSuccess
                    ? WorkflowStatus.Success
                    : WorkflowStatus.Running;
        }

        private static WorkflowStatus CalcAllComplete(bool hasFailure, bool hasSuccess, bool hasRunning, bool hasUnknown, bool hasAny)
        {
            return hasRunning || hasUnknown
                ? WorkflowStatus.Running
                : WorkflowStatus.Success;
        }

        private static WorkflowStatus CalcAnySucceed(bool hasFailure, bool hasSuccess, bool hasRunning, bool hasUnknown, bool hasAny)
        {
            return !hasAny
                ? WorkflowStatus.Failure
                : hasSuccess
                    ? WorkflowStatus.Success
                    : hasRunning || hasUnknown
                        ? WorkflowStatus.Running
                        : WorkflowStatus.Failure;
        }

        private delegate WorkflowStatus CalcStatus(bool hasFailure, bool hasSuccess, bool hasRunning, bool hasUndetermined, bool hasAny);
    }
}
