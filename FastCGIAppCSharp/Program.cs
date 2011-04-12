using System;
using System.Net;
using System.IO;
using System.Globalization;
using System.Text;

using FastCGI;

namespace FastCGIApp
{
    class Program
    {
        static void HandleRequest(Request request, Response response)
        {
            // receive HTTP content
            byte[] content = request.Stdin.GetContents();

            // access server variables
            string serverSoftware = request.ServerSoftware.GetValueOrDefault();
            string method = request.RequestMethod.Value;

            // access HTTP headers
            string userAgent = request.Headers[RequestHeader.HttpUserAgent];
            string cookieValue = request.GetCookieValue("Keks").GetValueOrDefault();

            // set HTTP headers
            response.SetHeader(ResponseHeader.HttpExpires,
                               Response.ToHttpDate(DateTime.Now.AddDays(1.0)));
            response.SetCookie(new Cookie("Keks", "yummy"));

            // send HTTP content
            response.PutStr(
                @"<html>
                   <body>
                    <p>Hello World!</p>
                    <p>Server: " + serverSoftware + @"</p>
                    <p>User Agent: " + userAgent + @"</p>
                    <p>Received cookie value: " + cookieValue + @"</p>
                    <p>Content length as read: " + content.Length + @"</P>
                    <p>Request method: " + method + @"</p>
                   </body>
                  </html>"
                );
        }

        static void Main(string[] args)
        {
            Options config = new Options();
            config.Bind = BindMode.CreateSocket;
            config.EndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 9000);
            config.OnError = Console.WriteLine;
            Server.Start(HandleRequest, config);
        }
    }
}
