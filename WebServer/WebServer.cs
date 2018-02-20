using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using static System.Console;

namespace Serac {
	public class WebServer {
		readonly List<(Func<Request, Task<Response>> Handler, string[] RootPath)> Handlers = new List<(Func<Request, Task<Response>> Handler, string[] RootPath)>();
		readonly List<Task> Listeners = new List<Task>();
		
		public WebServer RegisterHandler(Func<Request, Task<Response>> handler, string root) {
			var rootpath = root.Split('/');
			if(rootpath[0] == "")
				rootpath = rootpath.Skip(rootpath.Length > 1 && rootpath[1] == "" ? 2 : 1).ToArray();
			Handlers.Add((handler, rootpath));
			return this;
		}

		public WebServer RegisterHandler(Func<Request, Response> handler, string root) =>
			RegisterHandler(async request => handler(request), root);
		
		public WebServer ListenOn(int port, IPAddress ip=null) {
			Listeners.Add(Task.Run(async () => {
				var server = new TcpListener(ip ?? IPAddress.Any, port);
				server.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
				server.Start();
				while(true) {
					var client = await server.AcceptSocketAsync();
					Task.Run(() => HandleClient(client));
				}
			}));
			return this;
		}

		public void RunForever() => Task.WaitAll(Listeners.ToArray());

		async void HandleClient(Socket socket) {
			try {
				var stream = new NetworkStream(socket);

				var sr = new StreamReader(stream, Encoding.UTF8);
				var sw = new StreamWriter(stream, Encoding.UTF8);

				bool keepAlive = false;
				var first = true;

				while(keepAlive || first) {
					var request = await Request.Parse(stream, sr, sw);
					if(request == null)
						return;

					var path = request.RealPath.Split('/');
					if(path[0] == "")
						path = path.Skip(path.Length > 1 && path[1] == "" ? 2 : 1).ToArray();
					Response response = null;
					foreach(var (handler, rp) in Handlers) {
						if(rp.Length > path.Length || !rp.SequenceEqual(path.Take(rp.Length)))
							continue;
						request.Path = rp.Length == 0 ? request.RealPath : "/" + string.Join('/', path.Skip(rp.Length));
						response = await handler(request);
						if(!stream.CanWrite)
							return;
						break;
					}

					if(first) {
						keepAlive = request.Headers.ContainsKey("Connection") && request.Headers["Connection"] == "keep-alive";
						first = false;
					}
					
					if(response == null)
						response = new Response {StatusCode = 404, Body = "File not found"};
					if(keepAlive)
						response.Headers["Connection"] = "keep-alive";
					WriteLine($"Response status {response.StatusCode}");
					await response.Send(stream, sw);
				}
			} catch(IOException e) {
				if(!(e.InnerException is SocketException))
					throw;
			} finally {
				socket.Close();
			}
		}
	}
}