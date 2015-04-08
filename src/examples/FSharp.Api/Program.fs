open System

[<EntryPoint>]
let main _ =
    Supervisioning.main()
    Console.ReadLine() |> ignore
    0