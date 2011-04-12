
// Response headers and objects

namespace FastCGI

open System
open System.Collections.Generic
open System.Net

open Connection
open Cookies

exception HeadersAlreadySent
exception OutputAlreadyClosed


type ResponseHeader = 
   // Response headers
   | HttpAcceptRanges
   | HttpAge
   | HttpETag
   | HttpLocation
   | HttpProxyAuthenticate
   | HttpRetryAfter
   | HttpServer
   | HttpVary
   | HttpWWWAuthenticate
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
   | HttpSetCookie


/// Represents an HTTP response, including response status, headers and body.
/// Provides both blocking and asynchronous methods.
type Response(conn:Connection, requestID) =
   let log = conn.Log
   let utf8 = Text.UTF8Encoding()

   let headerToString = function
   | HttpExtensionHeader name -> name
   | HttpAcceptRanges -> "Accept-Ranges"
   | HttpAge -> "Age"
   | HttpETag -> "ETag"
   | HttpLocation -> "Location"
   | HttpProxyAuthenticate -> "Proxy-Authenticate"
   | HttpRetryAfter -> "Retry-After"
   | HttpServer -> "Server"
   | HttpVary -> "Vary"
   | HttpWWWAuthenticate -> "WWW-Authenticate"
   // Entity headers
   | HttpAllow -> "Allow"
   | HttpContentEncoding -> "Content-Encoding"
   | HttpContentLanguage -> "Content-Language"
   | HttpContentLength-> "Content-Length"
   | HttpContentLocation -> "Content-Location"
   | HttpContentMD5 -> "Content-MD5"
   | HttpContentRange -> "Content-Range"
   | HttpContentType -> "Content-Type"
   | HttpExpires -> "Expires"
   | HttpLastModified -> "Last-Modified"
   // Nonstandard headers
   | HttpSetCookie -> "Set-Cookie"

   // state
   let mutable responseStatus = 200
   let headers = new Dictionary<ResponseHeader, string>()
   let cookies = new Dictionary<string, Cookie>()
   do headers.[HttpContentType] <- "text/html; charset=utf-8"
   let mutable headersSent = false
   let mutable closed = false

   let requireHeadersNotSent() = if headersSent then raise HeadersAlreadySent

   let requireOutputNotClosed() = if closed then raise OutputAlreadyClosed
   
   let headersAsNameValuePairs() =
      headers
      |> Seq.map (fun (KeyValue(h,v)) -> headerToString h, v)
      |> List.ofSeq              

   let getHeaderData() =
      let headerPairs =
         [ yield "Status", string(responseStatus)
           yield! headersAsNameValuePairs()
           if not (headers.ContainsKey HttpSetCookie) then
              if cookies.Count > 0 then yield "Set-Cookie", printCookies cookies.Values ]
      if conn.Options.TraceResponseHeaders then
         for (name,value) in headerPairs do log <| sprintf "header %s, value: %s" name value
      let headersAsString = headerPairs
                              |> List.map (fun (n,v) -> n + ": " + v)
                              |> String.concat "\r\n"
      utf8.GetBytes(headersAsString + "\r\n\r\n")
   

   let sendHeaders() =
      requireOutputNotClosed()
      if not headersSent then
         log "SentHeaders"
         headersSent <- true
         Some (getHeaderData())
      else
         None


   /// <summary>Sets and gets the response status which will be or has been sent with the response headers.</summary>
   /// <exception cref="HeadersAlreadySent">Thrown if the response headers have already been sent.</exception>
   member x.ResponseStatus
      with get() = responseStatus
      and  set(v) = requireHeadersNotSent(); responseStatus <- v

   /// <summary>Sets the given 'HttpHeader' response header to the given string value, overriding
   /// any value which has previously been set.
   /// <para>The only headers set by default are "Status" (200) and "Content-Type" (text/html; charset=utf-8).</para>
   /// <para>If a value is set for the 'HttpSetCookie' header, this overrides all cookies set
   /// for this request with 'SetCookie' or 'UnsetCookie'.</para>
   /// </summary>
   /// <exception cref="HeadersAlreadySent">If the response headers have already been sent</exception>
   member x.SetHeader(header, value) =
      requireHeadersNotSent()
      headers.[header] <- value

   /// <summary>Causes the given 'HttpHeader' response header not to be sent, overriding any value
   /// which has previously been set.
   /// <para>Does not prevent the 'HttpSetCookie' header from being sent if cookies have been
   /// set for this request with 'SetCookie'.</para>
   /// </summary>
   /// <exception cref="HeadersAlreadySent">If the response headers have already been sent</exception>
   member x.UnsetHeader header =
      requireHeadersNotSent()
      headers.Remove header |> ignore

   /// <summary>Causes the user agent to record the given cookie and send it back with future loads of this page.
   /// The value will be put into quotes before it is sent to the client.
   /// <para>If an HttpCookie header is set for this request by a call to SetHeader, this function
   /// has not effect.</para></summary>
   /// <exception cref="HeadersAlreadySent">Thrown if the response headers have already been sent.</exception>
   /// <exception cref="CookieException">Thrown if the name is not valid.</exception>
   member x.SetCookie (cookie:Cookie) =
      requireHeadersNotSent()
      cookies.Add(cookie.Name, cookie)

   /// <summary>Causes the user agent to unset any cookie applicable to this page with the given name.
   /// <para>If an HttpCookie header is set for this request by a call to SetHeader, this function
   /// has not effect.</para></summary>
   /// <exception cref="HeadersAlreadySent">Thrown if the response headers have already been sent.</exception>
   /// <exception cref="CookieException">Thrown if the name is not valid.</exception>
   member x.UnsetCookie name =
      requireHeadersNotSent()
      let yesterday = DateTime.UtcNow - TimeSpan.FromDays(1.)
      let c = new Cookie(name, "", Expires=yesterday)
      cookies.Add(name, c)

   /// <summary>Ensures that the response headers have been sent.  If they are already sent, does
   /// nothing.</summary>
   /// <exception cref="OutputAlreadyClosed">If output has already been closed</exception>
   member x.SendHeaders() =
      match sendHeaders() with
      | Some data -> conn.SendBuffer requestID data
      | None -> ()

   /// <summary>Ensures that the response headers have been sent.  If they are already sent, does
   /// nothing.</summary>
   /// <exception cref="OutputAlreadyClosed">If output has already been closed</exception>
   member x.AsyncSendHeaders() =
      async {
         match sendHeaders() with
         | Some data -> do! conn.AsyncSendBuffer requestID data
         | None -> return ()
      }

   /// <summary>Sends data.  This is the content data of the HTTP response.
   /// <para>If the response headers have not been sent, first sends them.</para>
   /// </summary>
   /// <exception cref="OutputAlreadyClosed">Thrown if output has already been closed</exception>
   member x.Put data =
      requireOutputNotClosed()
      x.SendHeaders()
      conn.SendBuffer requestID data

   /// <summary>Asynchronously sends data.  This is the content data of the HTTP response.
   /// <para>If the response headers have not been sent, first sends them.</para>
   /// </summary>
   /// <exception cref="OutputAlreadyClosed">Thrown if output has already been closed</exception>
   member x.AsyncPut data =
      async {
         requireOutputNotClosed()
         do! x.AsyncSendHeaders()
         do! conn.AsyncSendBuffer requestID data
      }

   /// <summary>Sends text, encoded as UTF-8.  This is the content data of the HTTP response.
   /// <para>If the response headers have not been sent, first sends them.</para>
   /// </summary>
   /// <exception cref="OutputAlreadyClosed">Thrown if output has already been closed</exception>
   member x.PutStr (str:string) = 
      str |> utf8.GetBytes |> x.Put

   /// <summary>Asynchronously sends text, encoded as UTF-8.  This is the content data of the HTTP response.
   /// <para>If the response headers have not been sent, first sends them.</para>
   /// </summary>
   /// <exception cref="OutputAlreadyClosed">Thrown if output has already been closed</exception>
   member x.AsyncPutStr (str:string) = 
      str |> utf8.GetBytes |> x.AsyncPut

   /// <summary>Informs the web server and the user agent that the request has completed.
   /// <para>As a side-effect, any unread input is discarded and no more can be read.</para>
   /// <para>This is implicitly called, if it has not already been, after the handler returns; it
   /// may be useful within a handler if the handler wishes to return results and then
   /// perform time-consuming computations before exiting.</para>
   /// </summary>
   /// <exception cref="OutputAlreadyClosed">If output has already been closed</exception>
   member x.CloseOutput() =
      requireOutputNotClosed()
      closed <- true
      conn.EndRequest requestID

   /// <summary>Asynchronously informs the web server and the user agent that the request has completed.
   /// See 'CloseOutput' for details.
   /// </summary>
   member x.AsyncCloseOutput() =
      async {
         requireOutputNotClosed()
         closed <- true
         do! conn.AsyncEndRequest requestID
      }

   /// Whether the response is closed. See 'CloseOutput'.
   member x.Closed = closed

   /// Returns whether it is possible to write more data; ie, whether output has not
   /// yet been closed as by 'fCloseOutput'.
   member x.IsWritable = not closed

   /// Format a date according to "RFC 822, updated by RFC 1123"
   static member ToHttpDate (dt:DateTime) = dt.ToUniversalTime().ToString("r")

   /// <summary>Sets the HTTP/1.1 return status to 301 and sets the 'HttpLocation' header to
   /// the provided URL.</summary>
   /// <exception cref="HeadersAlreadySent">Thrown if the response headers have already been sent.</exception>
   member x.PermanentRedirect url = 
      x.ResponseStatus <- 301
      x.SetHeader (HttpLocation, url)

   /// <summary>Sets the HTTP/1.1 return status to 303 and sets the 'HttpLocation' header to
   /// the provided URL.</summary>
   /// <exception cref="HeadersAlreadySent">Thrown if the response headers have already been sent.</exception>
   member x.SeeOtherRedirect url =
      x.ResponseStatus <- 303
      x.SetHeader (HttpLocation, url)
