
// Dynamic loading wrapper around an optional C++ assembly.

module SocketSupportDynamicWrapper

open System
open System.Net.Sockets
open System.Reflection
open System.IO
open System.Runtime.CompilerServices

open Microsoft.Win32.SafeHandles

/// We wrap the access to this low level C++ assembly in
/// a dynamic access layer, because it is only needed if
/// a stdin socket is used for communication.
/// Otherwise, we do not need the SocketSupport assembly at all.
/// For documentation, see the C++/CLI project.
type SocketSupport private (assembly:Assembly) =
   static let assemblyFilename = "SocketSupport.dll"
   let classType = assembly.GetType("SocketSupport.SocketSupport")

   do RuntimeHelpers.RunClassConstructor classType.TypeHandle // call static constructor

   static let loadAssembly fullPath =
      try
         Some (Assembly.LoadFile fullPath)
      with
         | :? IOException | :? BadImageFormatException -> None

   static let getAssembly() =
      let home = Path.GetDirectoryName <| Assembly.GetExecutingAssembly().Location
      [Path.Combine(home, assemblyFilename)
       Path.Combine(home, @"..\..\..\SocketSupport\Release\", assemblyFilename)
       Path.Combine(home, @"..\..\..\SocketSupport\Debug\", assemblyFilename)
      ]
      |> List.fold
         (fun maybeAssembly path ->
            match maybeAssembly with
            | Some a -> Some a
            | None -> loadAssembly path)
         None

   static member Create() =
      getAssembly()
      |> Option.map (fun assembly -> new SocketSupport(assembly))

   member x.DuplicateStdinSocket =
      let meth = classType.GetMethod("DuplicateStdinSocket")
      fun () ->
         let socketInfo = new SocketInformation()
         let parameters = [|socketInfo :> obj|]
         let success =  meth.Invoke(null, parameters) |> unbox<bool>
         success, (parameters.[0] :?> SocketInformation)

   member x.EventSelectRead =
      let meth = classType.GetMethod("EventSelectRead")
      fun (socketHandle:IntPtr) -> meth.Invoke(null, [|socketHandle|]) :?> SafeWaitHandle

   member x.EventSelectWrite =
      let meth = classType.GetMethod("EventSelectWrite")
      fun (socketHandle:IntPtr) -> meth.Invoke(null, [|socketHandle|]) :?> SafeWaitHandle
