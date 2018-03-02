using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
		bool CompressionEnabled;

		public WebServer EnableCompression() => InlineUpdate(() => CompressionEnabled = true);
		public WebServer DisableCompression() => InlineUpdate(() => CompressionEnabled = false);
		
		public WebServer RegisterHandler(string root, Func<Request, Task<Response>> handler) {
			var rootpath = root.Split('/');
			if(rootpath[0] == "")
				rootpath = rootpath.Skip(rootpath.Length > 1 && rootpath[1] == "" ? 2 : 1).ToArray();
			Handlers.Add((handler, rootpath));
			return this;
		}

#pragma warning disable 1998
		public WebServer RegisterHandler(string root, Func<Request, Response> handler) =>
			RegisterHandler(root, async request => handler(request));
#pragma warning restore 1998
		
		public WebServer ListenOn(int port, IPAddress ip=null) {
			if(ip == null)
				WriteLine($"Listening on port {port} on all interfaces");
			else
				WriteLine($"Listening on {ip}:{port}");
			Listeners.Add(Task.Run(async () => {
				var server = new TcpListener(ip ?? IPAddress.Any, port);
				server.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
				server.Start();
				while(true) {
					var client = await server.AcceptSocketAsync();
#pragma warning disable 4014
					Task.Factory.StartNew(() => HandleClient(client), TaskCreationOptions.PreferFairness);
#pragma warning restore 4014
				}
			}));
			return this;
		}

		public void RunForever() => Task.WaitAll(Listeners.ToArray());

		async void HandleClient(Socket socket) {
			try {
				var stream = new NetworkStream(socket);

				var sr = new InternalStreamReader(stream);
				var sw = new StreamWriter(stream, Encoding.UTF8);

				bool keepAlive = false;
				var first = true;

				while(keepAlive || first) {
					var request = await Request.Parse(stream, sr, sw);
					if(request == null)
						return;

					var encodings = request.Headers.TryGetValue("Accept-Encoding", "").Split(",").Select(x => x.Trim());
					request.UseGzip = CompressionEnabled && encodings.Contains("gzip");

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
						if(response != null)
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

					if(request.UseGzip && response.Data == null)
						response.Gzipped = false;
					else if(request.UseGzip && !response.Gzipped && !response.NoCompression) {
						using(var wms = new MemoryStream()) {
							using(var rms = new MemoryStream(response.Data))
								using(var gs = new GZipStream(wms, CompressionLevel.Fastest)) {
									await rms.CopyToAsync(gs);
									await gs.FlushAsync();
								}
							response.Data = wms.ToArray();
							response.Gzipped = true;
						}
					}

					if(response.Gzipped && !response.Headers.ContainsKey("Content-Encoding"))
						response.Headers["Content-Encoding"] = "gzip";

					await response.Send(stream, sw);
				}
			} catch(IOException e) {
				if(!(e.InnerException is SocketException))
					throw;
			} finally {
				socket.Close();
			}
		}

		public WebServer InlineUpdate(Action cb) {
			cb();
			return this;
		}
	}
}