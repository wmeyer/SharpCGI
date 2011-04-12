
// Two socket classes that support F# friendly async methods.
// This is not done with extension methods because we need two different implementations.

module AsyncSocket

open System
open System.Net
open System.Net.Sockets
open System.Threading
open System.Timers

open SocketSupportDynamicWrapper

// (Implemented as a wrapping interface, not an abstract class, 
// because we don't want to reimplement BeginAccept/EndAccept which would have to return an AsyncSocket)
type AsyncSocket =
   inherit IDisposable

   abstract AsyncAccept : unit -> Async<AsyncSocket>
   abstract AsyncSendAll : byte[] -> Async<unit>
   abstract AsyncSendAll : byte[] * int * int -> Async<unit>
   abstract AsyncRecvAll : int -> Async<byte[]>

   // delegated to Socket
   abstract Bind : EndPoint -> unit
   abstract Listen : int -> unit
   abstract RemoteEndPoint : EndPoint
   abstract Blocking : bool with get, set
   abstract Connected : bool
   abstract Close : unit -> unit
   abstract Dispose : unit -> unit
   abstract Shutdown : SocketShutdown -> unit
   abstract Send : byte[] -> int
   abstract Send : byte[] * int * int * SocketFlags -> int



type AsyncSocketImpl(s:Socket) as this =
   let this = this :> AsyncSocket

   interface IDisposable with
      member x.Dispose() = s.Dispose()

   interface AsyncSocket with
      member x.AsyncAccept() =
         async {
            let! socket = Async.FromBeginEnd(s.BeginAccept, s.EndAccept)
            return (new AsyncSocketImpl(socket) :> AsyncSocket)
         }

      /// asynchronously send the given bytes; repeated sends until all done
      member x.AsyncSendAll(buffer:byte[]) = this.AsyncSendAll(buffer, 0, buffer.Length)

      member x.AsyncSendAll(buffer:byte[], offset, size) = 
         async {
            if size = 0 then return ()
            else
               let! sentLen = x.AsyncSend(buffer, offset, size)
               return! this.AsyncSendAll(buffer, offset+sentLen, size-sentLen)
         }

      // creates an async computation that will repeatedly try to receive data from
      // the socket until 'totalSize' bytes are received or 0 bytes are returned from the socket.
      member x.AsyncRecvAll totalSize =
         async {
            if totalSize = 0 then return Array.empty
            else
               let res = Array.create totalSize 0uy
               let! receiveLen = x.AsyncReceive res // Ex timeout res
               match receiveLen with
               | 0 -> return Array.empty
               | receivedSize when receivedSize = totalSize -> return res
               | receivedSize ->
                  let res' = Array.create receivedSize 0uy
                  Array.blit res 0 res' 0 receivedSize
                  let! rest = this.AsyncRecvAll (totalSize-receivedSize)
                  return Array.append res' rest
         }

      member x.Bind endPoint = s.Bind endPoint
      member x.Listen backlog = s.Listen backlog
      member x.RemoteEndPoint = s.RemoteEndPoint
      member x.Blocking 
         with get() = s.Blocking
         and set(v) = s.Blocking <- v
      member x.Connected = s.Connected
      member x.Close() = s.Close()
      member x.Dispose() = s.Dispose()
      member x.Shutdown how = s.Shutdown how
      member x.Send buffer = s.Send buffer
      member x.Send(buffer, offset, size, flags) = s.Send(buffer, offset, size, flags)


   member private x.AsyncSend(buffer, offset, size) =
      Async.FromBeginEnd(buffer, offset, size, 
         (fun (b, o, size, cb, st) -> s.BeginSend(b, o, size, SocketFlags.None, cb, st)),
         s.EndSend)

   member private x.AsyncReceive buffer =
      Async.FromBeginEnd( buffer, 
                          (fun (b, cb, stIgnored) ->
                              try
                                 s.BeginReceive(b, 0, b.Length, SocketFlags.None, cb, false)
                              with
                                 | :? System.ObjectDisposedException
                                 | :? System.Net.Sockets.SocketException ->
                                    { new IAsyncResult with
                                       member x.AsyncState = box true
                                       member x.AsyncWaitHandle = null
                                       member x.CompletedSynchronously = false
                                       member x.IsCompleted = true
                                    }
                          ),
                          (fun (res : IAsyncResult) ->
                              match res.AsyncState with
                              | :? System.Boolean as socketIsClosed when socketIsClosed = true -> 0
                              | _ ->
                                 try
                                    s.EndReceive res
                                 with
                                    | :? System.ObjectDisposedException
                                    | :? System.Net.Sockets.SocketException -> 0)) // don't want exception if socket closed



// Socket.BeginReceive and Socket.BeginSend return an error (INVALID_ARG) when used on the stdin socket delivered by IIS.
// This class works around that:
type AsyncSocketStdinImpl(s:Socket) as this =
   let this = this :> AsyncSocket

   static let staticSocketSupport = SocketSupport.Create() // load the DLL only once

   let socketSupport =
       match staticSocketSupport with
       | None -> failwith "Could not load \"SocketSupport.dll\" when trying to create an instance of AsyncSocketStdImpl"
       | Some s -> s

   interface IDisposable with
      member x.Dispose() = s.Dispose()

   interface AsyncSocket with
      member x.AsyncAccept() =
         async {
            let! socket = Async.FromBeginEnd(s.BeginAccept, s.EndAccept)
            return (new AsyncSocketStdinImpl(socket) :> AsyncSocket)
         }

      member x.AsyncSendAll(buffer:byte[]) = this.AsyncSendAll(buffer, 0, buffer.Length)

      member x.AsyncSendAll(buffer, offset, size) =
         async {
            if size = 0 then return ()
            else
               let! sentLen = x.AsyncSend(buffer, offset, size)
               return! this.AsyncSendAll (buffer, offset+sentLen, size-sentLen)
         }

      member x.AsyncRecvAll totalSize =
         async {
            if totalSize = 0 then return Array.empty
            else
               let res = Array.create totalSize 0uy
               let! receiveLen = x.AsyncReceive Timeout.Infinite res
               match receiveLen with
               | 0 -> return Array.empty
               | receivedSize when receivedSize = totalSize -> return res
               | receivedSize ->
                  let res' = Array.create receivedSize 0uy
                  Array.blit res 0 res' 0 receivedSize
                  let! rest = this.AsyncRecvAll (totalSize-receivedSize)
                  return Array.append res' rest
         }

      member x.Bind endPoint = s.Bind endPoint
      member x.Listen backlog = s.Listen backlog
      member x.RemoteEndPoint = s.RemoteEndPoint
      member x.Blocking 
         with get() = s.Blocking
         and set(v) = s.Blocking <- v
      member x.Connected = s.Connected
      member x.Close() = s.Close()
      member x.Dispose() = s.Dispose()
      member x.Shutdown how = s.Shutdown how
      member x.Send buffer = s.Send buffer
      member x.Send(buffer, offset, size, flags) = s.Send(buffer, offset, size, flags)


   member private x.AsyncSend(buffer, offset, size) =
      async {
         let! canWrite = x.AsyncSelectWrite()
         if canWrite then
            let sentLen = s.Send(buffer, offset, size, SocketFlags.None)
            return sentLen
         else
            return 0
      }

   member private x.AsyncReceive timeout (buffer:byte[]) =
      async {
         let! canRead = x.AsyncSelectRead timeout
         if canRead then
            let receiveLen = s.Receive buffer
            return receiveLen
         else
            return 0
      }

   member private x.AsyncSelectRead(timeout) =
      let safeWaitHandle = socketSupport.EventSelectRead(s.Handle)
      let waitHandle = new EventWaitHandle(false, EventResetMode.ManualReset, SafeWaitHandle=safeWaitHandle);
      Async.AwaitWaitHandle(waitHandle, timeout)

   member private x.AsyncSelectWrite() =
      let safeWaitHandle = socketSupport.EventSelectWrite(s.Handle)
      let waitHandle = new EventWaitHandle(false, EventResetMode.ManualReset, SafeWaitHandle=safeWaitHandle);
      Async.AwaitWaitHandle waitHandle
