// A small F# program: a greeting and the mean of some values.
module HelloTotals

let values = [ 1.5; 2.0; 32.5; -0.4; 10.0 ]

let mean (xs: float list) =
    match xs with
    | [] -> 0.0
    | _ -> List.sum xs / float (List.length xs)

type Greeter(name: string) =
    member this.Greet() = sprintf "Hello from %s" name

[<EntryPoint>]
let main argv =
    printfn "Hello from iTerminal"
    let total = mean values
    if total > 5.0 then printfn "Mean is large: %.3f" total
    else printfn "Mean is small"
    (* A block comment
       over two lines. *)
    0
