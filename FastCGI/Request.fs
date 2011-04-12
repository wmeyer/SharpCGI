
// Request headers and objects

namespace FastCGI

open System
open System.Net

open Cookies
open Record
open Connection
open VariableEncoding


type RequestHeader =
   // Request headers
   | HttpAccept
   | HttpAcceptCharset
   | HttpAcceptEncoding
   | HttpAcceptLanguage
   | HttpAuthorization
   | HttpExpect
   | HttpFrom
   | HttpHost
   | HttpIfMatch
   | HttpIfModifiedSince
   | HttpIfNoneMatch
   | HttpIfRange
   | HttpIfUnmodifiedSince
   | HttpMaxForwards
   | HttpProxyAuthorization
   | HttpRange
   | HttpReferer
   | HttpTE
   | HttpUserAgent
   // Entity headers
   | HttpAllow
   | HttpContentEncoding
   | HttpContentLanguage
   | HttpContentLength
   | HttpContentLocation
   | HttpContentMD5
   | HttpContentRange
   | HttpContentType
   | HttpExpires
   | HttpLastModified
   | HttpExtensionHeader of string
   // Nonstandard headers
   | HttpConnection
   | HttpCookie

module private HeaderFunctions = 
   let headerNameFromVariableName (name:string) =
      let titleCase (word:string) =
         Char.ToUpper(word.[0]).ToString() + word.Substring(1).ToLower()
      name.Substring(5).Split('_')
      |> Array.map titleCase
      |> String.concat "-"

   let headerFromVariableName = function
      | "HTTP_ACCEPT" -> Some HttpAccept
      | "HTTP_ACCEPT_CHARSET" -> Some HttpAcceptCharset
      | "HTTP_ACCEPT_ENCODING" -> Some HttpAcceptEncoding
      | "HTTP_ACCEPT_LANGUAGE" -> Some HttpAcceptLanguage
      | "HTTP_AUTHORIZATION" -> Some HttpAuthorization
      | "HTTP_EXPECT" -> Some HttpExpect
      | "HTTP_FROM" -> Some HttpFrom
      | "HTTP_HOST" -> Some HttpHost
      | "HTTP_IF_MATCH" -> Some HttpIfMatch
      | "HTTP_IF_MODIFIED_SINCE" -> Some HttpIfModifiedSince
      | "HTTP_IF_NONE_MATCH" -> Some HttpIfNoneMatch
      | "HTTP_IF_RANGE" -> Some HttpIfRange
      | "HTTP_IF_UNMODIFIED_SINCE" -> Some HttpIfUnmodifiedSince
      | "HTTP_MAX_FORWARDS" -> Some HttpMaxForwards
      | "HTTP_PROXY_AUTHORIZATION" -> Some HttpProxyAuthorization
      | "HTTP_RANGE" -> Some HttpRange
      | "HTTP_REFERER" -> Some HttpReferer
      | "HTTP_TE" -> Some HttpTE
      | "HTTP_USER_AGENT" -> Some HttpUserAgent
      | "HTTP_ALLOW" -> Some HttpAllow
      | "HTTP_CONTENT_ENCODING" -> Some HttpContentEncoding
      | "HTTP_CONTENT_LANGUAGE" -> Some HttpContentLanguage
      | "HTTP_CONTENT_LENGTH" -> Some HttpContentLength
      | "HTTP_CONTENT_LOCATION" -> Some HttpContentLocation
      | "HTTP_CONTENT_MD5" -> Some HttpContentMD5
      | "HTTP_CONTENT_RANGE" -> Some HttpContentRange
      | "HTTP_CONTENT_TYPE" -> Some HttpContentType
      | "HTTP_EXPIRES" -> Some HttpExpires
      | "HTTP_LAST_MODIFIED" -> Some HttpLastModified
      | "HTTP_CONNECTION" -> Some HttpConnection
      | "HTTP_COOKIE" -> Some HttpCookie
      | name when name.StartsWith "HTTP_" -> Some (HttpExtensionHeader (headerNameFromVariableName name))
      | _ -> None

open HeaderFunctions


// if a blocking read is tried after all data has been read
exception BufferIsClosed

/// <summary>Encapsulates the Stdin buffer of a request, i.e. the content data.
/// <para>Objects of this class are implicitely connected to a Response object (as dictated by the protocol).
/// If the response has been closed,  it it no longer possible to read additional
/// data from the request's Stdin stream.</para>
/// Provides both blocking and asynchronous methods.
/// </summary>
type StreamBuffer(conn:Connection, request:Request) =
   let mutable buffer = Array.empty<byte>
   let mutable offset = 0
   let mutable allRead = false

   let remainingSize() = buffer.Length - offset

   let requireOutputNotClosed() = if request.Completed then raise OutputAlreadyClosed

   let rec extendBuffer size =
      async {
         if not allRead then
            if size > remainingSize() then
               let! maybeRecord = conn.AsyncReceiveRecord request.ID
               match maybeRecord with
               | None -> ()
               | Some record ->
                  match record.Type with
                  | RecordType.StdinRecord ->
                     if record.Length > 0 then
                        buffer <- Array.append buffer record.Content
                        do! extendBuffer size
                     else
                        allRead <- true
                  | x -> conn.LogError <| sprintf "Buffer, extendBuffer: Ignoring record of unexpected type %A" x
      }

   let rec extendCompletely() =
      async {
         if not allRead then
            conn.Log "extendCompletely, before asyncreceiverecord"
            let! maybeRecord = conn.AsyncReceiveRecord request.ID
            conn.Log "extendCompletely, after asyncreceiverecord"
            match maybeRecord with
            | None -> allRead <- true
            | Some record ->
               match record.Type with
               | RecordType.StdinRecord ->
                  if record.Length > 0 then
                     buffer <- Array.append buffer record.Content
                     do! extendCompletely()
                  else
                     allRead <- true
               | x -> conn.LogError <| sprintf "Buffer, extendCompletely: Ignoring record of unexpected type %A" x
      }

   let takeBuffer size =
      assert (size <= remainingSize())
      if offset = 0 && size = buffer.Length then buffer
      else
         let result = Array.create size 0uy
         Array.blit buffer offset result 0 size
         offset <- offset + size
         result

   let get size =
      async {
         requireOutputNotClosed()
         if remainingSize() < size then
            do! extendBuffer size
         let actualSize = min size (remainingSize())
         return takeBuffer actualSize
      }


   // PUBLIC

   /// <summary>Reads up to a specified amount of data from the input stream of the current request,
   /// and interprets it as binary data. This is the content data of the HTTP request, if any.
   /// <para>If insufficient input is available, blocks until there is enough.</para>
   /// <para>If all input has been read, returns an empty array.</para>
   /// </summary>
   /// <exception cref="OutputAlreadyClosed">If CloseOutput of the current response has alread been called.</exception>
   member x.Get size = get size |> Async.RunSynchronously

   /// <summary>Asynchronously reads up to a specified amount of data from the input stream of the current request,
   /// and interprets it as binary data. This is the content data of the HTTP request, if any.
   /// See 'Get' for further details. </summary>
   member x.AsyncGet size = get size

   /// <summary>Reads all remaining data from the input stream of the current request, and
   /// interprets it as binary data.  This is the content data of the HTTP request, if any.
   /// <para>Blocks until all input has been read.</para>
   /// <para>If all input has been read, returns an empty array.</para>
   /// </summary>
   /// <exception cref="OutputAlreadyClosed">If CloseOutput of the current response has alread been called.</exception>
   member x.GetContents() =
      conn.Log "GetContents"
      requireOutputNotClosed()
      extendCompletely() |> Async.RunSynchronously
      takeBuffer (remainingSize())

   /// <summary>Asynchronously reads all remaining data from the input stream of the current request, and
   /// interprets it as binary data.  This is the content data of the HTTP request, if any.
   /// See 'GetContents' for further details. </summary>
   member x.AsyncGetContents() =
      async {
         requireOutputNotClosed()
         do! extendCompletely()
         return takeBuffer (remainingSize())
      }
         
   /// Returns whether the input stream of the current request potentially has data
   /// remaining, either in the buffer or yet to be read.         
   member x.IsReadable =
      remainingSize() > 0 || (not allRead) && (not request.Completed)



/// Represents an HTTP request, including request variables, headers (incl. cookies) and
/// the content data.
and Request(id, paramBuffer: byte array, conn:Connection, response: Response) as this =
   let log = conn.Options.TraceLogger
   let variables = decodeVariables conn.Options.VariableEncoding paramBuffer
   let variableMap = Map.ofList variables

   let tryFindVar n = Map.tryFind n variableMap

   let tryFindIntVar n = tryFindVar n |> Option.map int 

   let tryFindAddressVar n =
      match tryFindVar n with
      | Some addr -> try Some (IPAddress.Parse addr) with | :? FormatException -> None
      | None -> None

   let stdin = new StreamBuffer(conn, this)

   let headerList = variables
                    |> List.choose (fun (name, value) ->
                                       if conn.Options.TraceRequestHeaders then
                                          log <| sprintf "header %s, value: %s" name value
                                       match headerFromVariableName name with
                                       | Some header -> Some (header, value)
                                       | None -> None
                                   )

   let headers = Map.ofList headerList

   let cookies =   
      seq {
         for header, value in headerList do
            if header = HttpCookie then
               yield! parseCookies value
      }
      |> Seq.map (fun c -> c.Name, c)
      |> Map.ofSeq

   // PUBLIC

   member x.ID = id

   member x.Stdin = stdin

   member x.Completed = response.Closed

   /// Request variables
   member x.Variables = variableMap

   /// HTTP/1.1 headers sent by the user agent
   member x.Headers = headers

   /// Map of 'cookies' provided by the user agent for this request.
   member x.Cookies = cookies

   /// A convenience method; as 'getCookie', but returns only the value of the cookie
   /// rather than a 'Cookie' object.
   member x.GetCookieValue name =
      match cookies.TryFind name with
      | None -> None
      | Some cookie -> Some cookie.Value

   // convenience methods for request variables

   member x.AuthentificationType = tryFindVar "AUTH_TYPE"
   member x.ContentLength = tryFindIntVar "CONTENT_LENGTH"

   member x.ContentType =
      match tryFindVar "CONTENT_TYPE" with
      | Some ct -> try Some (Mime.ContentType(ct)) with | :? FormatException -> None
      | None -> None

   member x.DocumentRoot = tryFindVar "DOCUMENT_ROOT"
   member x.GatewayInterface = tryFindVar "GATEWAY_INTERFACE"
   member x.PathInfo = tryFindVar "PATH_INFO"
   member x.PathTranslated = tryFindVar "PATH_TRANSLATED"
   member x.QueryString = tryFindVar "QUERY_STRING"
   member x.RedirectStatus = tryFindIntVar "REDIRECT_STATUS"
   member x.RedirectURL = tryFindVar "REDIRECT_URL"
   member x.RemoteAddress = tryFindAddressVar "REMOTE_ADDRESS"
   member x.RemotePort = tryFindIntVar "REMOTE_PORT"
   member x.RemoteHost = tryFindVar "REMOTE_HOST"
   member x.RemoteIdent = tryFindVar "REMOTE_IDENT"
   member x.RemoteUser = tryFindVar "REMOTE_USER"

   member x.RequestMethod = tryFindVar "REQUEST_METHOD"
   member x.RequestURI = tryFindVar "REQUEST_URI"
   member x.ScriptFilename = tryFindVar "SCRIPT_FILENAME"
   member x.ScriptName = tryFindVar "SCRIPT_NAME"
   member x.ServerAddress = tryFindAddressVar "SERVER_ADDR"
   member x.ServerName = tryFindVar "SERVER_NAME"
   member x.ServerPort = tryFindIntVar "SERVER_PORT"
   member x.ServerProcotol = tryFindVar "SERVER_PROTOCOL"
   member x.ServerSoftware = tryFindVar "SERVER_SOFTWARE"

   member x.Url = tryFindVar "URL"
