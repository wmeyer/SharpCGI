
// Configuration options.
// (This file is designed to be usable from C#.)

namespace FastCGI

open System.Net

/// A logging delegate type used for error logging and tracing.
type LoggerDelegate = delegate of string -> unit

/// <summary>How to listen to connections from the web server.
/// <para>UseStdinSocket: try to use stdin as a socket (as described in the FastCGI spec and for example implemented by IIS)</para>
/// <para>CreateSocket: create a new socket, as specified with the EndPoint property.</para>
/// </summary>
type BindMode =
 | UseStdinSocket = 0
 | CreateSocket = 1

/// <summary>Collection of configuration options for the FastCGI server.
/// You should always at least specify a logging function (ErrorLogger (F#) or OnError(C#))
/// and a Bind method.</summary>
type Options() =
   let NoOpLogger = fun (str:string) -> ()
   let mutable bindType = BindMode.UseStdinSocket
   let mutable endPoint : IPEndPoint = null
   let mutable listenBacklog = 1000
   let mutable errorLogger : string -> unit = NoOpLogger
   let mutable traceLogger : string -> unit = NoOpLogger
   let mutable traceRequestHeaders = false
   let mutable traceResponseHeaders = false
   let mutable catchHandlerExceptions = true
   let mutable MAX_CONNS = "10000"
   let mutable MAX_REQS = "10000"
   let mutable MPXS_CONNS = "1"
   let mutable concurrentConnections = true
   let mutable variableEncoding = System.Text.UTF8Encoding()

   /// How to listen to connections from the web server.
   /// If this is set to CreateSocket, you MUST specify a value for EndPoint.
   member x.Bind
      with get() = bindType
      and set(v) = bindType <- v
   
   /// Address and port of the socket if CreateSocket is chosen for Bind.
   member x.EndPoint
      with get() = endPoint
      and set(v) = endPoint <- v

   /// Maximum listen backlog. See Socket.Listen documentation.
   member x.ListenBacklog 
      with get() = listenBacklog
      and set(v) = listenBacklog <- v

   /// A delegate that is executed with a string message whenever an unexpected error occurs.
   member x.OnError
      with set(v:LoggerDelegate) = errorLogger <- v.Invoke

   /// A F# function that is called with a string message whenever an unexpected error occurs.
   member x.ErrorLogger 
      with get() = errorLogger
      and set(v) = errorLogger <- v

   /// A delegate that receives tracing information (for debugging).
   member x.OnTrace
      with set(v:LoggerDelegate) = traceLogger <- v.Invoke

   /// A F# function that receives tracing information (for debugging).
   member x.TraceLogger 
      with get() = traceLogger
      and set(v) = traceLogger <- v

   /// Whether to print the received HTTP headers with TraceLogger.
   member x.TraceRequestHeaders
      with get() = traceRequestHeaders
      and set(v) = traceRequestHeaders <- v

   /// Whether to print the response HTTP headers with TraceLogger.
   member x.TraceResponseHeaders
      with get() = traceResponseHeaders
      and set(v) = traceResponseHeaders <- v

   /// Whether to catch exceptions raised by client code (in the handler function)
   member x.CatchHandlerExceptions
      with get() = catchHandlerExceptions
      and set(v) = catchHandlerExceptions <- v

   /// What to reply to the server when it asks about FCGI_MAX_CONNS (not implemented by most servers)
   member x.FCGI_MAX_CONNS
      with get() = MAX_CONNS
      and set(v) = MAX_CONNS <- v

   /// What to reply to the server when it asks about FCGI_MAX_REQS (not implemented by most servers)
   member x.FCGI_MAX_REQS
      with get() = MAX_REQS
      and set(v) = MAX_REQS <- v

   /// What to reply to the server when it asks about FCGI_MPXS_CONNS (not implemented by most servers)
   member x.FCGI_MPXS_CONNS
      with get() = MPXS_CONNS
      and set(v) = MPXS_CONNS <- v

   /// Whether to allow multiple connections at the same time.
   /// Settings this to false can be useful if your handler code is not thread-safe, but makes it impossible
   /// to exploit concurrency even if the web server supports parallel connections.
   member x.ConcurrentConnections
      with get() = concurrentConnections
      and set(v) = concurrentConnections <- v

   /// Text encoding of the variables in FastCGI records. Default: UTF8
   member x.VariableEncoding
      with get() = variableEncoding
      and set(v) = variableEncoding <- v
