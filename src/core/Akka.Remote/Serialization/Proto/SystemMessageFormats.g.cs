// Generated by the protocol buffer compiler.  DO NOT EDIT!
// source: SystemMessageFormats.proto
#pragma warning disable 1591, 0612, 3021
#region Designer generated code

using pb = global::Google.Protobuf;
using pbc = global::Google.Protobuf.Collections;
using pbr = global::Google.Protobuf.Reflection;
using scg = global::System.Collections.Generic;
namespace Akka.Remote.Serialization.Proto.Msg {

  /// <summary>Holder for reflection information generated from SystemMessageFormats.proto</summary>
  internal static partial class SystemMessageFormatsReflection {

    #region Descriptor
    /// <summary>File descriptor for SystemMessageFormats.proto</summary>
    public static pbr::FileDescriptor Descriptor {
      get { return descriptor; }
    }
    private static pbr::FileDescriptor descriptor;

    static SystemMessageFormatsReflection() {
      byte[] descriptorData = global::System.Convert.FromBase64String(
          string.Concat(
            "ChpTeXN0ZW1NZXNzYWdlRm9ybWF0cy5wcm90bxIjQWtrYS5SZW1vdGUuU2Vy",
            "aWFsaXphdGlvbi5Qcm90by5Nc2caFkNvbnRhaW5lckZvcm1hdHMucHJvdG8i",
            "TwoKQ3JlYXRlRGF0YRJBCgVjYXVzZRgBIAEoCzIyLkFra2EuUmVtb3RlLlNl",
            "cmlhbGl6YXRpb24uUHJvdG8uTXNnLkV4Y2VwdGlvbkRhdGEiUQoMUmVjcmVh",
            "dGVEYXRhEkEKBWNhdXNlGAEgASgLMjIuQWtrYS5SZW1vdGUuU2VyaWFsaXph",
            "dGlvbi5Qcm90by5Nc2cuRXhjZXB0aW9uRGF0YSJPCgpSZXN1bWVEYXRhEkEK",
            "BWNhdXNlGAEgASgLMjIuQWtrYS5SZW1vdGUuU2VyaWFsaXphdGlvbi5Qcm90",
            "by5Nc2cuRXhjZXB0aW9uRGF0YSJgCg1TdXBlcnZpc2VEYXRhEkAKBWNoaWxk",
            "GAEgASgLMjEuQWtrYS5SZW1vdGUuU2VyaWFsaXphdGlvbi5Qcm90by5Nc2cu",
            "QWN0b3JSZWZEYXRhEg0KBWFzeW5jGAIgASgIIpMBCglXYXRjaERhdGESQgoH",
            "d2F0Y2hlZRgBIAEoCzIxLkFra2EuUmVtb3RlLlNlcmlhbGl6YXRpb24uUHJv",
            "dG8uTXNnLkFjdG9yUmVmRGF0YRJCCgd3YXRjaGVyGAIgASgLMjEuQWtrYS5S",
            "ZW1vdGUuU2VyaWFsaXphdGlvbi5Qcm90by5Nc2cuQWN0b3JSZWZEYXRhIp4B",
            "CgpGYWlsZWREYXRhEkAKBWNoaWxkGAEgASgLMjEuQWtrYS5SZW1vdGUuU2Vy",
            "aWFsaXphdGlvbi5Qcm90by5Nc2cuQWN0b3JSZWZEYXRhEkEKBWNhdXNlGAIg",
            "ASgLMjIuQWtrYS5SZW1vdGUuU2VyaWFsaXphdGlvbi5Qcm90by5Nc2cuRXhj",
            "ZXB0aW9uRGF0YRILCgN1aWQYAyABKAQilQEKGkRlYXRoV2F0Y2hOb3RpZmlj",
            "YXRpb25EYXRhEkAKBWFjdG9yGAEgASgLMjEuQWtrYS5SZW1vdGUuU2VyaWFs",
            "aXphdGlvbi5Qcm90by5Nc2cuQWN0b3JSZWZEYXRhEhoKEmV4aXN0ZW5jZUNv",
            "bmZpcm1lZBgCIAEoCBIZChFhZGRyZXNzVGVybWluYXRlZBgDIAEoCGIGcHJv",
            "dG8z"));
      descriptor = pbr::FileDescriptor.FromGeneratedCode(descriptorData,
          new pbr::FileDescriptor[] { global::Akka.Remote.Serialization.Proto.Msg.ContainerFormatsReflection.Descriptor, },
          new pbr::GeneratedClrTypeInfo(null, new pbr::GeneratedClrTypeInfo[] {
            new pbr::GeneratedClrTypeInfo(typeof(global::Akka.Remote.Serialization.Proto.Msg.CreateData), global::Akka.Remote.Serialization.Proto.Msg.CreateData.Parser, new[]{ "Cause" }, null, null, null),
            new pbr::GeneratedClrTypeInfo(typeof(global::Akka.Remote.Serialization.Proto.Msg.RecreateData), global::Akka.Remote.Serialization.Proto.Msg.RecreateData.Parser, new[]{ "Cause" }, null, null, null),
            new pbr::GeneratedClrTypeInfo(typeof(global::Akka.Remote.Serialization.Proto.Msg.ResumeData), global::Akka.Remote.Serialization.Proto.Msg.ResumeData.Parser, new[]{ "Cause" }, null, null, null),
            new pbr::GeneratedClrTypeInfo(typeof(global::Akka.Remote.Serialization.Proto.Msg.SuperviseData), global::Akka.Remote.Serialization.Proto.Msg.SuperviseData.Parser, new[]{ "Child", "Async" }, null, null, null),
            new pbr::GeneratedClrTypeInfo(typeof(global::Akka.Remote.Serialization.Proto.Msg.WatchData), global::Akka.Remote.Serialization.Proto.Msg.WatchData.Parser, new[]{ "Watchee", "Watcher" }, null, null, null),
            new pbr::GeneratedClrTypeInfo(typeof(global::Akka.Remote.Serialization.Proto.Msg.FailedData), global::Akka.Remote.Serialization.Proto.Msg.FailedData.Parser, new[]{ "Child", "Cause", "Uid" }, null, null, null),
            new pbr::GeneratedClrTypeInfo(typeof(global::Akka.Remote.Serialization.Proto.Msg.DeathWatchNotificationData), global::Akka.Remote.Serialization.Proto.Msg.DeathWatchNotificationData.Parser, new[]{ "Actor", "ExistenceConfirmed", "AddressTerminated" }, null, null, null)
          }));
    }
    #endregion

  }
  #region Messages
  internal sealed partial class CreateData : pb::IMessage<CreateData> {
    private static readonly pb::MessageParser<CreateData> _parser = new pb::MessageParser<CreateData>(() => new CreateData());
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public static pb::MessageParser<CreateData> Parser { get { return _parser; } }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public static pbr::MessageDescriptor Descriptor {
      get { return global::Akka.Remote.Serialization.Proto.Msg.SystemMessageFormatsReflection.Descriptor.MessageTypes[0]; }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    pbr::MessageDescriptor pb::IMessage.Descriptor {
      get { return Descriptor; }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public CreateData() {
      OnConstruction();
    }

    partial void OnConstruction();

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public CreateData(CreateData other) : this() {
      Cause = other.cause_ != null ? other.Cause.Clone() : null;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public CreateData Clone() {
      return new CreateData(this);
    }

    /// <summary>Field number for the "cause" field.</summary>
    public const int CauseFieldNumber = 1;
    private global::Akka.Remote.Serialization.Proto.Msg.ExceptionData cause_;
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public global::Akka.Remote.Serialization.Proto.Msg.ExceptionData Cause {
      get { return cause_; }
      set {
        cause_ = value;
      }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public override bool Equals(object other) {
      return Equals(other as CreateData);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public bool Equals(CreateData other) {
      if (ReferenceEquals(other, null)) {
        return false;
      }
      if (ReferenceEquals(other, this)) {
        return true;
      }
      if (!object.Equals(Cause, other.Cause)) return false;
      return true;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public override int GetHashCode() {
      int hash = 1;
      if (cause_ != null) hash ^= Cause.GetHashCode();
      return hash;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public override string ToString() {
      return pb::JsonFormatter.ToDiagnosticString(this);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public void WriteTo(pb::CodedOutputStream output) {
      if (cause_ != null) {
        output.WriteRawTag(10);
        output.WriteMessage(Cause);
      }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public int CalculateSize() {
      int size = 0;
      if (cause_ != null) {
        size += 1 + pb::CodedOutputStream.ComputeMessageSize(Cause);
      }
      return size;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public void MergeFrom(CreateData other) {
      if (other == null) {
        return;
      }
      if (other.cause_ != null) {
        if (cause_ == null) {
          cause_ = new global::Akka.Remote.Serialization.Proto.Msg.ExceptionData();
        }
        Cause.MergeFrom(other.Cause);
      }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public void MergeFrom(pb::CodedInputStream input) {
      uint tag;
      while ((tag = input.ReadTag()) != 0) {
        switch(tag) {
          default:
            input.SkipLastField();
            break;
          case 10: {
            if (cause_ == null) {
              cause_ = new global::Akka.Remote.Serialization.Proto.Msg.ExceptionData();
            }
            input.ReadMessage(cause_);
            break;
          }
        }
      }
    }

  }

  internal sealed partial class RecreateData : pb::IMessage<RecreateData> {
    private static readonly pb::MessageParser<RecreateData> _parser = new pb::MessageParser<RecreateData>(() => new RecreateData());
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public static pb::MessageParser<RecreateData> Parser { get { return _parser; } }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public static pbr::MessageDescriptor Descriptor {
      get { return global::Akka.Remote.Serialization.Proto.Msg.SystemMessageFormatsReflection.Descriptor.MessageTypes[1]; }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    pbr::MessageDescriptor pb::IMessage.Descriptor {
      get { return Descriptor; }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public RecreateData() {
      OnConstruction();
    }

    partial void OnConstruction();

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public RecreateData(RecreateData other) : this() {
      Cause = other.cause_ != null ? other.Cause.Clone() : null;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public RecreateData Clone() {
      return new RecreateData(this);
    }

    /// <summary>Field number for the "cause" field.</summary>
    public const int CauseFieldNumber = 1;
    private global::Akka.Remote.Serialization.Proto.Msg.ExceptionData cause_;
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public global::Akka.Remote.Serialization.Proto.Msg.ExceptionData Cause {
      get { return cause_; }
      set {
        cause_ = value;
      }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public override bool Equals(object other) {
      return Equals(other as RecreateData);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public bool Equals(RecreateData other) {
      if (ReferenceEquals(other, null)) {
        return false;
      }
      if (ReferenceEquals(other, this)) {
        return true;
      }
      if (!object.Equals(Cause, other.Cause)) return false;
      return true;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public override int GetHashCode() {
      int hash = 1;
      if (cause_ != null) hash ^= Cause.GetHashCode();
      return hash;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public override string ToString() {
      return pb::JsonFormatter.ToDiagnosticString(this);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public void WriteTo(pb::CodedOutputStream output) {
      if (cause_ != null) {
        output.WriteRawTag(10);
        output.WriteMessage(Cause);
      }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public int CalculateSize() {
      int size = 0;
      if (cause_ != null) {
        size += 1 + pb::CodedOutputStream.ComputeMessageSize(Cause);
      }
      return size;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public void MergeFrom(RecreateData other) {
      if (other == null) {
        return;
      }
      if (other.cause_ != null) {
        if (cause_ == null) {
          cause_ = new global::Akka.Remote.Serialization.Proto.Msg.ExceptionData();
        }
        Cause.MergeFrom(other.Cause);
      }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public void MergeFrom(pb::CodedInputStream input) {
      uint tag;
      while ((tag = input.ReadTag()) != 0) {
        switch(tag) {
          default:
            input.SkipLastField();
            break;
          case 10: {
            if (cause_ == null) {
              cause_ = new global::Akka.Remote.Serialization.Proto.Msg.ExceptionData();
            }
            input.ReadMessage(cause_);
            break;
          }
        }
      }
    }

  }

  internal sealed partial class ResumeData : pb::IMessage<ResumeData> {
    private static readonly pb::MessageParser<ResumeData> _parser = new pb::MessageParser<ResumeData>(() => new ResumeData());
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public static pb::MessageParser<ResumeData> Parser { get { return _parser; } }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public static pbr::MessageDescriptor Descriptor {
      get { return global::Akka.Remote.Serialization.Proto.Msg.SystemMessageFormatsReflection.Descriptor.MessageTypes[2]; }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    pbr::MessageDescriptor pb::IMessage.Descriptor {
      get { return Descriptor; }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public ResumeData() {
      OnConstruction();
    }

    partial void OnConstruction();

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public ResumeData(ResumeData other) : this() {
      Cause = other.cause_ != null ? other.Cause.Clone() : null;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public ResumeData Clone() {
      return new ResumeData(this);
    }

    /// <summary>Field number for the "cause" field.</summary>
    public const int CauseFieldNumber = 1;
    private global::Akka.Remote.Serialization.Proto.Msg.ExceptionData cause_;
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public global::Akka.Remote.Serialization.Proto.Msg.ExceptionData Cause {
      get { return cause_; }
      set {
        cause_ = value;
      }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public override bool Equals(object other) {
      return Equals(other as ResumeData);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public bool Equals(ResumeData other) {
      if (ReferenceEquals(other, null)) {
        return false;
      }
      if (ReferenceEquals(other, this)) {
        return true;
      }
      if (!object.Equals(Cause, other.Cause)) return false;
      return true;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public override int GetHashCode() {
      int hash = 1;
      if (cause_ != null) hash ^= Cause.GetHashCode();
      return hash;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public override string ToString() {
      return pb::JsonFormatter.ToDiagnosticString(this);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public void WriteTo(pb::CodedOutputStream output) {
      if (cause_ != null) {
        output.WriteRawTag(10);
        output.WriteMessage(Cause);
      }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public int CalculateSize() {
      int size = 0;
      if (cause_ != null) {
        size += 1 + pb::CodedOutputStream.ComputeMessageSize(Cause);
      }
      return size;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public void MergeFrom(ResumeData other) {
      if (other == null) {
        return;
      }
      if (other.cause_ != null) {
        if (cause_ == null) {
          cause_ = new global::Akka.Remote.Serialization.Proto.Msg.ExceptionData();
        }
        Cause.MergeFrom(other.Cause);
      }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public void MergeFrom(pb::CodedInputStream input) {
      uint tag;
      while ((tag = input.ReadTag()) != 0) {
        switch(tag) {
          default:
            input.SkipLastField();
            break;
          case 10: {
            if (cause_ == null) {
              cause_ = new global::Akka.Remote.Serialization.Proto.Msg.ExceptionData();
            }
            input.ReadMessage(cause_);
            break;
          }
        }
      }
    }

  }

  internal sealed partial class SuperviseData : pb::IMessage<SuperviseData> {
    private static readonly pb::MessageParser<SuperviseData> _parser = new pb::MessageParser<SuperviseData>(() => new SuperviseData());
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public static pb::MessageParser<SuperviseData> Parser { get { return _parser; } }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public static pbr::MessageDescriptor Descriptor {
      get { return global::Akka.Remote.Serialization.Proto.Msg.SystemMessageFormatsReflection.Descriptor.MessageTypes[3]; }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    pbr::MessageDescriptor pb::IMessage.Descriptor {
      get { return Descriptor; }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public SuperviseData() {
      OnConstruction();
    }

    partial void OnConstruction();

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public SuperviseData(SuperviseData other) : this() {
      Child = other.child_ != null ? other.Child.Clone() : null;
      async_ = other.async_;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public SuperviseData Clone() {
      return new SuperviseData(this);
    }

    /// <summary>Field number for the "child" field.</summary>
    public const int ChildFieldNumber = 1;
    private global::Akka.Remote.Serialization.Proto.Msg.ActorRefData child_;
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public global::Akka.Remote.Serialization.Proto.Msg.ActorRefData Child {
      get { return child_; }
      set {
        child_ = value;
      }
    }

    /// <summary>Field number for the "async" field.</summary>
    public const int AsyncFieldNumber = 2;
    private bool async_;
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public bool Async {
      get { return async_; }
      set {
        async_ = value;
      }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public override bool Equals(object other) {
      return Equals(other as SuperviseData);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public bool Equals(SuperviseData other) {
      if (ReferenceEquals(other, null)) {
        return false;
      }
      if (ReferenceEquals(other, this)) {
        return true;
      }
      if (!object.Equals(Child, other.Child)) return false;
      if (Async != other.Async) return false;
      return true;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public override int GetHashCode() {
      int hash = 1;
      if (child_ != null) hash ^= Child.GetHashCode();
      if (Async != false) hash ^= Async.GetHashCode();
      return hash;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public override string ToString() {
      return pb::JsonFormatter.ToDiagnosticString(this);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public void WriteTo(pb::CodedOutputStream output) {
      if (child_ != null) {
        output.WriteRawTag(10);
        output.WriteMessage(Child);
      }
      if (Async != false) {
        output.WriteRawTag(16);
        output.WriteBool(Async);
      }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public int CalculateSize() {
      int size = 0;
      if (child_ != null) {
        size += 1 + pb::CodedOutputStream.ComputeMessageSize(Child);
      }
      if (Async != false) {
        size += 1 + 1;
      }
      return size;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public void MergeFrom(SuperviseData other) {
      if (other == null) {
        return;
      }
      if (other.child_ != null) {
        if (child_ == null) {
          child_ = new global::Akka.Remote.Serialization.Proto.Msg.ActorRefData();
        }
        Child.MergeFrom(other.Child);
      }
      if (other.Async != false) {
        Async = other.Async;
      }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public void MergeFrom(pb::CodedInputStream input) {
      uint tag;
      while ((tag = input.ReadTag()) != 0) {
        switch(tag) {
          default:
            input.SkipLastField();
            break;
          case 10: {
            if (child_ == null) {
              child_ = new global::Akka.Remote.Serialization.Proto.Msg.ActorRefData();
            }
            input.ReadMessage(child_);
            break;
          }
          case 16: {
            Async = input.ReadBool();
            break;
          }
        }
      }
    }

  }

  internal sealed partial class WatchData : pb::IMessage<WatchData> {
    private static readonly pb::MessageParser<WatchData> _parser = new pb::MessageParser<WatchData>(() => new WatchData());
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public static pb::MessageParser<WatchData> Parser { get { return _parser; } }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public static pbr::MessageDescriptor Descriptor {
      get { return global::Akka.Remote.Serialization.Proto.Msg.SystemMessageFormatsReflection.Descriptor.MessageTypes[4]; }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    pbr::MessageDescriptor pb::IMessage.Descriptor {
      get { return Descriptor; }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public WatchData() {
      OnConstruction();
    }

    partial void OnConstruction();

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public WatchData(WatchData other) : this() {
      Watchee = other.watchee_ != null ? other.Watchee.Clone() : null;
      Watcher = other.watcher_ != null ? other.Watcher.Clone() : null;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public WatchData Clone() {
      return new WatchData(this);
    }

    /// <summary>Field number for the "watchee" field.</summary>
    public const int WatcheeFieldNumber = 1;
    private global::Akka.Remote.Serialization.Proto.Msg.ActorRefData watchee_;
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public global::Akka.Remote.Serialization.Proto.Msg.ActorRefData Watchee {
      get { return watchee_; }
      set {
        watchee_ = value;
      }
    }

    /// <summary>Field number for the "watcher" field.</summary>
    public const int WatcherFieldNumber = 2;
    private global::Akka.Remote.Serialization.Proto.Msg.ActorRefData watcher_;
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public global::Akka.Remote.Serialization.Proto.Msg.ActorRefData Watcher {
      get { return watcher_; }
      set {
        watcher_ = value;
      }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public override bool Equals(object other) {
      return Equals(other as WatchData);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public bool Equals(WatchData other) {
      if (ReferenceEquals(other, null)) {
        return false;
      }
      if (ReferenceEquals(other, this)) {
        return true;
      }
      if (!object.Equals(Watchee, other.Watchee)) return false;
      if (!object.Equals(Watcher, other.Watcher)) return false;
      return true;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public override int GetHashCode() {
      int hash = 1;
      if (watchee_ != null) hash ^= Watchee.GetHashCode();
      if (watcher_ != null) hash ^= Watcher.GetHashCode();
      return hash;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public override string ToString() {
      return pb::JsonFormatter.ToDiagnosticString(this);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public void WriteTo(pb::CodedOutputStream output) {
      if (watchee_ != null) {
        output.WriteRawTag(10);
        output.WriteMessage(Watchee);
      }
      if (watcher_ != null) {
        output.WriteRawTag(18);
        output.WriteMessage(Watcher);
      }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public int CalculateSize() {
      int size = 0;
      if (watchee_ != null) {
        size += 1 + pb::CodedOutputStream.ComputeMessageSize(Watchee);
      }
      if (watcher_ != null) {
        size += 1 + pb::CodedOutputStream.ComputeMessageSize(Watcher);
      }
      return size;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public void MergeFrom(WatchData other) {
      if (other == null) {
        return;
      }
      if (other.watchee_ != null) {
        if (watchee_ == null) {
          watchee_ = new global::Akka.Remote.Serialization.Proto.Msg.ActorRefData();
        }
        Watchee.MergeFrom(other.Watchee);
      }
      if (other.watcher_ != null) {
        if (watcher_ == null) {
          watcher_ = new global::Akka.Remote.Serialization.Proto.Msg.ActorRefData();
        }
        Watcher.MergeFrom(other.Watcher);
      }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public void MergeFrom(pb::CodedInputStream input) {
      uint tag;
      while ((tag = input.ReadTag()) != 0) {
        switch(tag) {
          default:
            input.SkipLastField();
            break;
          case 10: {
            if (watchee_ == null) {
              watchee_ = new global::Akka.Remote.Serialization.Proto.Msg.ActorRefData();
            }
            input.ReadMessage(watchee_);
            break;
          }
          case 18: {
            if (watcher_ == null) {
              watcher_ = new global::Akka.Remote.Serialization.Proto.Msg.ActorRefData();
            }
            input.ReadMessage(watcher_);
            break;
          }
        }
      }
    }

  }

  internal sealed partial class FailedData : pb::IMessage<FailedData> {
    private static readonly pb::MessageParser<FailedData> _parser = new pb::MessageParser<FailedData>(() => new FailedData());
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public static pb::MessageParser<FailedData> Parser { get { return _parser; } }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public static pbr::MessageDescriptor Descriptor {
      get { return global::Akka.Remote.Serialization.Proto.Msg.SystemMessageFormatsReflection.Descriptor.MessageTypes[5]; }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    pbr::MessageDescriptor pb::IMessage.Descriptor {
      get { return Descriptor; }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public FailedData() {
      OnConstruction();
    }

    partial void OnConstruction();

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public FailedData(FailedData other) : this() {
      Child = other.child_ != null ? other.Child.Clone() : null;
      Cause = other.cause_ != null ? other.Cause.Clone() : null;
      uid_ = other.uid_;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public FailedData Clone() {
      return new FailedData(this);
    }

    /// <summary>Field number for the "child" field.</summary>
    public const int ChildFieldNumber = 1;
    private global::Akka.Remote.Serialization.Proto.Msg.ActorRefData child_;
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public global::Akka.Remote.Serialization.Proto.Msg.ActorRefData Child {
      get { return child_; }
      set {
        child_ = value;
      }
    }

    /// <summary>Field number for the "cause" field.</summary>
    public const int CauseFieldNumber = 2;
    private global::Akka.Remote.Serialization.Proto.Msg.ExceptionData cause_;
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public global::Akka.Remote.Serialization.Proto.Msg.ExceptionData Cause {
      get { return cause_; }
      set {
        cause_ = value;
      }
    }

    /// <summary>Field number for the "uid" field.</summary>
    public const int UidFieldNumber = 3;
    private ulong uid_;
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public ulong Uid {
      get { return uid_; }
      set {
        uid_ = value;
      }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public override bool Equals(object other) {
      return Equals(other as FailedData);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public bool Equals(FailedData other) {
      if (ReferenceEquals(other, null)) {
        return false;
      }
      if (ReferenceEquals(other, this)) {
        return true;
      }
      if (!object.Equals(Child, other.Child)) return false;
      if (!object.Equals(Cause, other.Cause)) return false;
      if (Uid != other.Uid) return false;
      return true;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public override int GetHashCode() {
      int hash = 1;
      if (child_ != null) hash ^= Child.GetHashCode();
      if (cause_ != null) hash ^= Cause.GetHashCode();
      if (Uid != 0UL) hash ^= Uid.GetHashCode();
      return hash;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public override string ToString() {
      return pb::JsonFormatter.ToDiagnosticString(this);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public void WriteTo(pb::CodedOutputStream output) {
      if (child_ != null) {
        output.WriteRawTag(10);
        output.WriteMessage(Child);
      }
      if (cause_ != null) {
        output.WriteRawTag(18);
        output.WriteMessage(Cause);
      }
      if (Uid != 0UL) {
        output.WriteRawTag(24);
        output.WriteUInt64(Uid);
      }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public int CalculateSize() {
      int size = 0;
      if (child_ != null) {
        size += 1 + pb::CodedOutputStream.ComputeMessageSize(Child);
      }
      if (cause_ != null) {
        size += 1 + pb::CodedOutputStream.ComputeMessageSize(Cause);
      }
      if (Uid != 0UL) {
        size += 1 + pb::CodedOutputStream.ComputeUInt64Size(Uid);
      }
      return size;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public void MergeFrom(FailedData other) {
      if (other == null) {
        return;
      }
      if (other.child_ != null) {
        if (child_ == null) {
          child_ = new global::Akka.Remote.Serialization.Proto.Msg.ActorRefData();
        }
        Child.MergeFrom(other.Child);
      }
      if (other.cause_ != null) {
        if (cause_ == null) {
          cause_ = new global::Akka.Remote.Serialization.Proto.Msg.ExceptionData();
        }
        Cause.MergeFrom(other.Cause);
      }
      if (other.Uid != 0UL) {
        Uid = other.Uid;
      }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public void MergeFrom(pb::CodedInputStream input) {
      uint tag;
      while ((tag = input.ReadTag()) != 0) {
        switch(tag) {
          default:
            input.SkipLastField();
            break;
          case 10: {
            if (child_ == null) {
              child_ = new global::Akka.Remote.Serialization.Proto.Msg.ActorRefData();
            }
            input.ReadMessage(child_);
            break;
          }
          case 18: {
            if (cause_ == null) {
              cause_ = new global::Akka.Remote.Serialization.Proto.Msg.ExceptionData();
            }
            input.ReadMessage(cause_);
            break;
          }
          case 24: {
            Uid = input.ReadUInt64();
            break;
          }
        }
      }
    }

  }

  internal sealed partial class DeathWatchNotificationData : pb::IMessage<DeathWatchNotificationData> {
    private static readonly pb::MessageParser<DeathWatchNotificationData> _parser = new pb::MessageParser<DeathWatchNotificationData>(() => new DeathWatchNotificationData());
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public static pb::MessageParser<DeathWatchNotificationData> Parser { get { return _parser; } }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public static pbr::MessageDescriptor Descriptor {
      get { return global::Akka.Remote.Serialization.Proto.Msg.SystemMessageFormatsReflection.Descriptor.MessageTypes[6]; }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    pbr::MessageDescriptor pb::IMessage.Descriptor {
      get { return Descriptor; }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public DeathWatchNotificationData() {
      OnConstruction();
    }

    partial void OnConstruction();

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public DeathWatchNotificationData(DeathWatchNotificationData other) : this() {
      Actor = other.actor_ != null ? other.Actor.Clone() : null;
      existenceConfirmed_ = other.existenceConfirmed_;
      addressTerminated_ = other.addressTerminated_;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public DeathWatchNotificationData Clone() {
      return new DeathWatchNotificationData(this);
    }

    /// <summary>Field number for the "actor" field.</summary>
    public const int ActorFieldNumber = 1;
    private global::Akka.Remote.Serialization.Proto.Msg.ActorRefData actor_;
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public global::Akka.Remote.Serialization.Proto.Msg.ActorRefData Actor {
      get { return actor_; }
      set {
        actor_ = value;
      }
    }

    /// <summary>Field number for the "existenceConfirmed" field.</summary>
    public const int ExistenceConfirmedFieldNumber = 2;
    private bool existenceConfirmed_;
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public bool ExistenceConfirmed {
      get { return existenceConfirmed_; }
      set {
        existenceConfirmed_ = value;
      }
    }

    /// <summary>Field number for the "addressTerminated" field.</summary>
    public const int AddressTerminatedFieldNumber = 3;
    private bool addressTerminated_;
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public bool AddressTerminated {
      get { return addressTerminated_; }
      set {
        addressTerminated_ = value;
      }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public override bool Equals(object other) {
      return Equals(other as DeathWatchNotificationData);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public bool Equals(DeathWatchNotificationData other) {
      if (ReferenceEquals(other, null)) {
        return false;
      }
      if (ReferenceEquals(other, this)) {
        return true;
      }
      if (!object.Equals(Actor, other.Actor)) return false;
      if (ExistenceConfirmed != other.ExistenceConfirmed) return false;
      if (AddressTerminated != other.AddressTerminated) return false;
      return true;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public override int GetHashCode() {
      int hash = 1;
      if (actor_ != null) hash ^= Actor.GetHashCode();
      if (ExistenceConfirmed != false) hash ^= ExistenceConfirmed.GetHashCode();
      if (AddressTerminated != false) hash ^= AddressTerminated.GetHashCode();
      return hash;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public override string ToString() {
      return pb::JsonFormatter.ToDiagnosticString(this);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public void WriteTo(pb::CodedOutputStream output) {
      if (actor_ != null) {
        output.WriteRawTag(10);
        output.WriteMessage(Actor);
      }
      if (ExistenceConfirmed != false) {
        output.WriteRawTag(16);
        output.WriteBool(ExistenceConfirmed);
      }
      if (AddressTerminated != false) {
        output.WriteRawTag(24);
        output.WriteBool(AddressTerminated);
      }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public int CalculateSize() {
      int size = 0;
      if (actor_ != null) {
        size += 1 + pb::CodedOutputStream.ComputeMessageSize(Actor);
      }
      if (ExistenceConfirmed != false) {
        size += 1 + 1;
      }
      if (AddressTerminated != false) {
        size += 1 + 1;
      }
      return size;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public void MergeFrom(DeathWatchNotificationData other) {
      if (other == null) {
        return;
      }
      if (other.actor_ != null) {
        if (actor_ == null) {
          actor_ = new global::Akka.Remote.Serialization.Proto.Msg.ActorRefData();
        }
        Actor.MergeFrom(other.Actor);
      }
      if (other.ExistenceConfirmed != false) {
        ExistenceConfirmed = other.ExistenceConfirmed;
      }
      if (other.AddressTerminated != false) {
        AddressTerminated = other.AddressTerminated;
      }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    public void MergeFrom(pb::CodedInputStream input) {
      uint tag;
      while ((tag = input.ReadTag()) != 0) {
        switch(tag) {
          default:
            input.SkipLastField();
            break;
          case 10: {
            if (actor_ == null) {
              actor_ = new global::Akka.Remote.Serialization.Proto.Msg.ActorRefData();
            }
            input.ReadMessage(actor_);
            break;
          }
          case 16: {
            ExistenceConfirmed = input.ReadBool();
            break;
          }
          case 24: {
            AddressTerminated = input.ReadBool();
            break;
          }
        }
      }
    }

  }

  #endregion

}

#endregion Designer generated code
