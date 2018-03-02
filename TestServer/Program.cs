using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serac;
using Serac.Katatonic;
using Serac.Static;
using Serac.Template;
using Serac.WebSockets;
using static System.Console;

namespace TestServer {
	[Handler]
	internal class Root : Katatonic {
		[Get]
		async Task<(string, string)> Index() {
			if(!Session.Values.ContainsKey("test"))
				Session.Values["test"] = "0";
			Session.Values["test"] = $"{int.Parse(Session.Values["test"]) + 1}";
			WriteLine($"Getting index ... ? {Session.Values["test"]}");
			return (
				"text/html", 
				"<form method=\"POST\" action=\"postTest\"><input type=\"text\" name=\"somePostParam\"><input type=\"submit\">"
			);
		}

		[Get]
		async Task<Response> GetTest(string strParam, int intParam, bool boolParam, int defParam = 5) {
			WriteLine($"Root/getTest '{strParam}' {intParam} {defParam} {boolParam}");
			return new Response {StatusCode = 200, Body = "Testing testing"};
		}

		[Post]
		async Task<(string, string)> PostTest(string somePostParam) {
			WriteLine($"Post!");
			return ("text/plain", $"Got post! {somePostParam}");
		}
	}

	public class TestTemplateModel {
		public string Title;
		public List<string> List = new List<string>();
		public int Expr;
	}

	[Handler("/test")]
	internal class Test : Katatonic {
		[Get]
		[Template("templates/test.kaml")]
		async Task<TestTemplateModel> Index() {
			return new TestTemplateModel { Title="Testing Title", List={ "Element 1", "Element deux" }, Expr=123 };
		}
		
		[Get]
		[Template("templates/test.kaml")]
		TestTemplateModel Sync() {
			return new TestTemplateModel { Title="Synchronous", List={ "Some elements", "From synchronous method" }, Expr=456 };
		}
	}
	
	class Program {
		static void Main(string[] args) {
			new WebServer()
				.EnableCompression()
				.EnableStaticCache()
				.WebSocket("/socket", async (ws, request) => {
					ws.Disconnect += (_, fromClient) => WriteLine($"{(fromClient ? "Client" : "Server")} disconnected");
					
					WriteLine($"Message from client: '{await ws.ReadText()}'");
					await ws.Write(request.Path);
					await ws.Write("Hello!");
					await ws.Write(new byte[] {0x41, 0x42});
					//await ws.Close();
					while(true)
						await ws.Write(await ws.ReadText());
				})
				.StaticFile("/favicon.ico", "./static/images/favicon.ico")
				.Katatonic("/katatonic", app => {
					app.Register<Root>();
					app.Register<Test>();
				})
				.RegisterHandler("/", StaticContent.Serve("./static"))
				.ListenOn(12345)
				.RunForever();
		}
	}
}