using System.Threading.Tasks;
using Serac;
using Serac.Static;
using Serac.WebSockets;
using static System.Console;

namespace TestServer {
	class Program {
		static void Main(string[] args) {
			new WebServer()
				.WebSocket(async (ws, request) => {
					ws.Disconnect += (_, fromClient) => WriteLine($"{(fromClient ? "Client" : "Server")} disconnected");
					
					WriteLine($"Message from client: '{await ws.ReadText()}'");
					await ws.Write(request.Path);
					await ws.Write("Hello!");
					await ws.Write(new byte[] {0x41, 0x42});
					//await ws.Close();
					while(true)
						await ws.Write(await ws.ReadText());
				}, "/socket")
				.ServeStaticFile("./static/images/favicon.ico", "/favicon.ico")
				.RegisterHandler(Static.Serve("./static"), "/")
				.ListenOn(12345)
				.RunForever();
		}
	}
}