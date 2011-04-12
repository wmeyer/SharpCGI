open System
open System.Net

open FastCGI

let configuration = Options(Bind = BindMode.CreateSocket,
                            EndPoint = IPEndPoint(IPAddress.Parse("127.0.0.1"), 9000),
                            ErrorLogger = (Console.WriteLine : string -> unit))

startServer configuration
   (fun request response ->
      // receive HTTP content
      let content = request.Stdin.GetContents()

      // access server variables
      let serverSoftware = match request.ServerSoftware with
                           | None -> "unknown"
                           | Some s -> s
      let reqMethod = request.RequestMethod.Value

      // access HTTP headers
      let userAgent = request.Headers.[HttpUserAgent]
      let cookieValue = match request.GetCookieValue "Keks" with
                        | None -> "unset"
                        | Some c -> c

      // set HTTP headers
      response.SetHeader (ResponseHeader.HttpExpires, Response.ToHttpDate (DateTime.Now.AddDays 1.0))
      response.SetCookie (Cookie("Keks", "yummy"))

      // send HTTP content
      @"<html>
         <body>
         <p>Hello World!</p>
         <p>Server: " + serverSoftware + @"</p>
         <p>User Agent: " + userAgent + @"</p>
         <p>Received cookie value: " + cookieValue + @"</p>
         <p>Content length as read: " + string(content.Length) + @"</P>
         <p>Request method: " + reqMethod + @"</p>
         </body>
      </html>"
      |> response.PutStr 
   )