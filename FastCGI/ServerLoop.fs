
// Listening, accepting and spawning connections.

module ServerLoop

open System
open System.Net
open System.Net.Sockets

open FastCGI
open SocketSupportDynamicWrapper
open AsyncSocket

/// Try to create the listen socket according to the configured options.
let createListenSocket (options:Options) : AsyncSocket option = 
   match options.Bind with
   | BindMode.UseStdinSocket ->
         match SocketSupport.Create() with
         | None -> let msg = "Cannot load \"SocketSupport.dll\" (needed when using binding option \"UseStdinSocket\")."
                   options.ErrorLogger msg
                   failwith msg
         | Some socketSupport ->
            match socketSupport.DuplicateStdinSocket() with
            | false, _ -> None
            | true, socketInfo -> Some (upcast new AsyncSocketStdinImpl(new Socket(socketInfo)))
   | BindMode.CreateSocket ->
         let socket = new Socket(AddressFamily.InterNetwork, 
                                 SocketType.Stream,
                                 ProtocolType.Tcp)
         let socket = new AsyncSocketImpl(socket) :> AsyncSocket
         socket.Bind options.EndPoint
         socket.Listen options.ListenBacklog
         Some socket
   | _ -> failwith "CreateListenSocket, unexpected bind mode"


let computeWebServerAddresses() =
   match Environment.GetEnvironmentVariable("FCGI_WEB_SERVER_ADDRS") with
   | null -> None
   | addrs -> Some( addrs.Split(',') |> Array.map IPAddress.Parse )


let validatePeerAddress (serverAddresses: IPAddress[] option) peer =
   if peer = null then true
   else
      match serverAddresses with
      | None -> true
      | Some addrs -> addrs |> Seq.exists ((=) peer)


let accept errorLogger (listenSocket:AsyncSocket) =
   async {
      try
         let! socket = listenSocket.AsyncAccept()
         return (Some socket)
      with
         | ex -> errorLogger <| sprintf "Error while accepting: %s" (ex.ToString())
                 return None
   }


let generalServerLoop connectionLoop (options:Options) handler = 
   let log = options.TraceLogger
   let logE = options.ErrorLogger
   let webServerAddresses = computeWebServerAddresses()
   
   match createListenSocket options with
   | None -> failwith "Socket creation failed"
   | Some ls ->
      async {
         use listenSocket = ls
         while true do
            log "acceptLoop, accepting"
            let! maybeSocket = accept logE listenSocket
            match maybeSocket with
            | None -> logE "acceptLoop, accept failed"
            | Some socket ->
               log "acceptLoop, accepted"
               if validatePeerAddress webServerAddresses (socket.RemoteEndPoint :?> IPEndPoint).Address then
                  log "acceptLoop, valid peer address"
                  if options.ConcurrentConnections then
                     log "starting concurrent connection"
                     Async.Start (connectionLoop socket options handler)
                  else
                     log "starting blocking connection"
                     do! (connectionLoop socket options handler)
               else
                  logE "acceptLoop, invalid peer address"
      }
