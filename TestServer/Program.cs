using System;
using Serac;
using Serac.Static;
using static System.Console;

namespace TestServer {
	class Program {
		static void Main(string[] args) {
			new WebServer()
				.RegisterHandler(Static.Serve("./"), "/static")
				.ServeStatic("assets", "/assets")
				.RegisterHandler(request => new Response{ StatusCode = 200, Body = $"Hello! {request.Path} ({request.RealPath})" }, "/foo")
				.RegisterHandler(request => new Response{ StatusCode = 200, Body = $"<h1>Hello! {request.Path} ({request.RealPath})</h1>", ContentType = "text/html" }, "/")
				.ListenOn(12345)
				.RunForever();
		}
	}
}