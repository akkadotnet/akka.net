open System

[<EntryPoint>]
let main args =
    Supervisioning.main()
    Console.ReadLine() |> ignore
    0