using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace DuoServer
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            const string publicDomain = "www";

            Console.Clear();
            var httpServer = new SimpleHttpServer(publicDomain);
            Console.Title = $"{GetIp()}: {httpServer.Port}";
            Console.WriteLine($"Server started on: {GetIp()} : {httpServer.Port}\n");
        }

        private static string GetIp()
        {
            var hostName = Dns.GetHostName();
            var ipEntry = Dns.GetHostEntry(hostName);
            var addresses = ipEntry.AddressList;
            return addresses.Last().ToString();
        }

        public class SimpleHttpServer
        {
            private readonly string[] _indexFiles = {
                "index.html",
                "index.htm",
                "default.html",
                "default.htm",
                "index.php"
            };

            private static readonly IDictionary<string, string> MimeTypeMappings = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase) {

            #region extension to MIME type list
                {".asf", "video/x-ms-asf"},
                {".asx", "video/x-ms-asf"},
                {".avi", "video/x-msvideo"},
                {".bin", "application/octet-stream"},
                {".cco", "application/x-cocoa"},
                {".crt", "application/x-x509-ca-cert"},
                {".css", "text/css"},
                {".deb", "application/octet-stream"},
                {".der", "application/x-x509-ca-cert"},
                {".dll", "application/octet-stream"},
                {".dmg", "application/octet-stream"},
                {".ear", "application/java-archive"},
                {".eot", "application/octet-stream"},
                {".exe", "application/octet-stream"},
                {".flv", "video/x-flv"},
                {".gif", "image/gif"},
                {".hqx", "application/mac-binhex40"},
                {".htc", "text/x-component"},
                {".htm", "text/html"},
                {".html", "text/html"},
                {".ico", "image/x-icon"},
                {".img", "application/octet-stream"},
                {".iso", "application/octet-stream"},
                {".jar", "application/java-archive"},
                {".jardiff", "application/x-java-archive-diff"},
                {".jng", "image/x-jng"},
                {".jnlp", "application/x-java-jnlp-file"},
                {".jpeg", "image/jpeg"},
                {".jpg", "image/jpeg"},
                {".js", "application/x-javascript"},
                {".mml", "text/mathml"},
                {".mng", "video/x-mng"},
                {".mov", "video/quicktime"},
                {".mp3", "audio/mpeg"},
                {".mpeg", "video/mpeg"},
                {".mpg", "video/mpeg"},
                {".msi", "application/octet-stream"},
                {".msm", "application/octet-stream"},
                {".msp", "application/octet-stream"},
                {".pdb", "application/x-pilot"},
                {".pdf", "application/pdf"},
                {".pem", "application/x-x509-ca-cert"},
                {".pl", "application/x-perl"},
                {".pm", "application/x-perl"},
                {".png", "image/png"},
                {".prc", "application/x-pilot"},
                {".ra", "audio/x-realaudio"},
                {".rar", "application/x-rar-compressed"},
                {".rpm", "application/x-redhat-package-manager"},
                {".rss", "text/xml"},
                {".run", "application/x-makeself"},
                {".sea", "application/x-sea"},
                {".shtml", "text/html"},
                {".sit", "application/x-stuffit"},
                {".swf", "application/x-shockwave-flash"},
                {".tcl", "application/x-tcl"},
                {".tk", "application/x-tcl"},
                {".txt", "text/plain"},
                {".war", "application/java-archive"},
                {".wbmp", "image/vnd.wap.wbmp"},
                {".wmv", "video/x-ms-wmv"},
                {".xml", "text/xml"},
                {".xpi", "application/x-xpinstall"},
                {".zip", "application/zip"},
            #endregion extension to MIME type list
            };

            private Thread _serverThread;
            private string _rootDirectory;
            private HttpListener _listener;

            public int Port { get; private set; }

            /// <summary>
            /// Construct server with given port.
            /// </summary>
            /// <param name="path">Directory path to serve.</param>
            /// <param name="port">Port of the server.</param>
            public SimpleHttpServer(string path, int port)
            {
                Initialize(path, port);
            }

            /// <summary>
            /// Construct server with suitable port.
            /// </summary>
            /// <param name="path">Directory path to serve.</param>
            public SimpleHttpServer(string path)
            {
                //get an empty port
                var tcpListener = new TcpListener(IPAddress.Loopback, 0);

                tcpListener.Start();
                var port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
                tcpListener.Stop();
                Initialize(path, port);
            }

            /// <summary>
            /// Stop server and dispose all functions.
            /// </summary>
            public void Stop()
            {
                _serverThread.Abort();
                _listener.Stop();
            }

            private void Listen()
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://*:{Port}/");
                _listener.Start();

                Console.WriteLine("Listener started at " + DateTime.Now);
                while (true)
                {
                    try
                    {
                        var context = _listener.GetContext();
                        Process(context);
                    }
                    catch (Exception exception)
                    {
                        Console.WriteLine("Listener Error: " + exception.Message);
                        _listener.Stop();
                    }
                }
            }

            private void Process(HttpListenerContext context)
            {
                var fileUrl = context.Request.Url;
                var filePath = fileUrl.AbsolutePath;

                Console.WriteLine("Requested: " + filePath);
                filePath = filePath.Substring(1);

                if (string.IsNullOrEmpty(filePath))
                {
                    Console.WriteLine("Error: Filename.IsNullOrEmpty at " + DateTime.Now);
                    foreach (var indexFile in _indexFiles)
                    {
                        if (File.Exists(Path.Combine(_rootDirectory, indexFile)))
                        {
                            filePath = indexFile;
                            break;
                        }
                    }
                }

                filePath = Path.Combine(_rootDirectory, filePath);

                if (File.Exists(filePath))
                {
                    try
                    {
                        Stream input = new FileStream(filePath, FileMode.Open);

                        //Adding permanent http response headers
                        context.Response.ContentType =
                            MimeTypeMappings.TryGetValue(Path.GetExtension(filePath), out var mime)
                                ? mime
                                : "application/octet-stream";
                        context.Response.ContentLength64 = input.Length;
                        context.Response.AddHeader("Date", DateTime.Now.ToString("r"));
                        context.Response.AddHeader("Last-Modified", File.GetLastWriteTime(filePath).ToString("r"));

                        var buffer = new byte[1024 * 16];
                        int bytesCount;
                        while ((bytesCount = input.Read(buffer, 0, buffer.Length)) > 0)
                            context.Response.OutputStream.Write(buffer, 0, bytesCount);
                        input.Close();

                        context.Response.StatusCode = (int)HttpStatusCode.OK;
                        context.Response.OutputStream.Flush();
                        Console.WriteLine("Info: Request Served + Flushed at " + DateTime.Now);
                    }
                    catch (Exception ex)
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                        Console.WriteLine("Warning: Internal Server Error Code: " + ex + " " + DateTime.Now);
                        Stop();
                    }
                }
                else
                {
                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    Console.WriteLine("Warning: Internal Server Error Code: Path not found at " + DateTime.Now);
                }

                context.Response.OutputStream.Close();
                Console.WriteLine("Info: Stream.Closed\n");
                Stop();
            }

            private void Initialize(string path, int port)
            {
                var stopwatch = Stopwatch.StartNew();
                ThreadStart childCallback = RunServer;
                Console.WriteLine("Loading Duo Server Pre-Alpha 1.00\n");
                _rootDirectory = path;
                Port = port;
                Console.WriteLine($"Server Starting with parameters: {Port}  _rootDirectory");
                _serverThread = new Thread(Listen);
                _serverThread.Start();
                Console.WriteLine("Server Thread Started Successfully");
                var childThread = new Thread(childCallback);
                childThread.Start();

                void RunServer()
                {
                    Console.WriteLine("Input Thread Started");
                    while (true)
                    {
                        var input = Console.ReadLine();
                        switch (input)
                        {
                            case "clear":
                                Console.Clear();
                                break;

                            case "ip":
                                Console.WriteLine(Console.Title);
                                break;

                            case "stop":
                                Stop();
                                Environment.Exit(1);
                                return;

                            case "test":
                                System.Diagnostics.Process.Start(@"http://" + Console.Title);
                                break;

                            case "starttime":
                                Console.WriteLine(stopwatch.Elapsed);
                                break;

                            default:
                                Console.WriteLine("Not a Command. Check case sensitivity");
                                break;
                        }
                    }
                }
            }
        }
    }
}
