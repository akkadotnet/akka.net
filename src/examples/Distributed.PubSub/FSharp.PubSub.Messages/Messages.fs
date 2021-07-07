namespace FSharp.PubSub.Messages

open ProtoBuf

[<Struct>]
type Publish = {
    [<ProtoMember(1)>] ResultCode_Removed : int64
    [<ProtoMember(2)>] Message : string
    [<ProtoMember(3)>] Id : int
}

[<Struct>]
type Message = {
    [<ProtoMember(1)>] ResultCode_Removed : int64
    [<ProtoMember(2)>] From : string
    [<ProtoMember(3)>] Text : string
    [<ProtoMember(4)>] Id : int
}
