using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using static System.Console;

namespace Serac.Katatonic {
	public class App {
		public readonly Dictionary<(string Method, string Path), Func<Request, Task<Response>>> Handlers = 
			new Dictionary<(string Method, string Path), Func<Request, Task<Response>>>();
		
		internal App(Action<App> builder) {
			builder(this);
		}
		
		internal async Task<Response> Handle(Request request) {
			WriteLine($"");
			var key = (request.Method, request.Path);
			return Handlers.ContainsKey(key) ? await Handlers[key](request) : null;
		}

		public App Register<HandlerT>() where HandlerT : new() {
			var instance = new HandlerT();
			var itype = typeof(HandlerT);
			var attr = (HandlerAttribute[]) itype.GetCustomAttributes(typeof(HandlerAttribute), false);
			if(attr.Length != 1)
				throw new NotSupportedException();
			var path = attr[0].Path;
			

			foreach(var meth in itype.GetRuntimeMethods()) {
				var mattra = (MethodAttribute[]) meth.GetCustomAttributes(typeof(MethodAttribute), true);
				if(mattra.Length != 1)
					continue;
				var mattr = mattra[0];
				var method = mattr.GetType().Name;
				method = method.Substring(0, method.Length - 9).ToUpper();
				var name = mattr.Name;
				if(name == null && meth.Name != "Index")
					name = $"{meth.Name.Substring(0, 1).ToLower()}{meth.Name.Substring(1)}";

				var tpath = name == null ? path : $"{path}{(path.EndsWith("/") ? "" : "/")}{name}";
				WriteLine($"Handler {meth.Name} has path '{tpath}'");
				Handlers[(method, tpath)] = BuildHandler(method, instance, meth);
			}
			
			return this;
		}

		class NonexistentArgument {
			internal static readonly NonexistentArgument Instance = new NonexistentArgument();
		}

		Func<Request, Task<Response>> BuildHandler(string rmethod, object instance, MethodInfo method) {
			Func<Request, object> IntParam(string name, bool hasDefault, int def) {
				if(rmethod == "POST") {
					if(hasDefault)
						return request => request.PostParameters.ContainsKey(name)
							? (int.TryParse(request.PostParameters[name], out var val) ? val : def)
							: def;
					else
						return request =>
							request.PostParameters.ContainsKey(name) && int.TryParse(request.PostParameters[name], out var val)
								? (object) val
								: NonexistentArgument.Instance;
				} else {
					if(hasDefault)
						return request => request.Query.ContainsKey(name)
							? (int.TryParse(request.Query[name], out var val) ? val : def)
							: def;
					else
						return request =>
							request.Query.ContainsKey(name) && int.TryParse(request.Query[name], out var val)
								? (object) val
								: NonexistentArgument.Instance;
				}
			}

			Func<Request, object> StringParam(string name, bool hasDefault, string def) {
				if(rmethod == "POST") {
					if(hasDefault)
						return request => request.PostParameters.ContainsKey(name)
							? request.PostParameters[name]
							: def;
					else
						return request =>
							request.PostParameters.ContainsKey(name)
								? (object) request.PostParameters[name]
								: NonexistentArgument.Instance;
				} else {
					return hasDefault
						? (Func<Request, object>) (request => request.Query.ContainsKey(name)
							? request.Query[name]
							: def)
						: (request =>
							request.Query.ContainsKey(name)
								? (object) request.Query[name]
								: NonexistentArgument.Instance);
				}
			}

			var argFuncs = new List<Func<Request, object>>();

			foreach(var arg in method.GetParameters()) {
				if(arg.ParameterType == typeof(int))
					argFuncs.Add(IntParam(arg.Name, arg.HasDefaultValue, arg.HasDefaultValue ? (int) arg.DefaultValue : 0));
				else if(arg.ParameterType == typeof(string))
					argFuncs.Add(StringParam(arg.Name, arg.HasDefaultValue, arg.HasDefaultValue ? (string) arg.DefaultValue : null));
				else
					throw new ArgumentException();
			}
			
			object[] ArgBuilder(Request request) {
				var args = argFuncs.Select(x => x(request)).ToArray();
				return args.Contains(NonexistentArgument.Instance) ? null : args;
			}
			
			if(method.ReturnType == typeof(Task))
				return async request => {
					var args = ArgBuilder(request);
					if(args == null) return null;
					await (Task) method.Invoke(instance, args);
					return new Response {StatusCode = 200};
				};
			else if(method.ReturnType == typeof(Task<Response>))
				return async request => {
					var args = ArgBuilder(request);
					if(args == null) return null;
					return await (Task<Response>) method.Invoke(instance, args);
				};
			else if(method.ReturnType == typeof(Task<string>))
				return async request => {
					var args = ArgBuilder(request);
					if(args == null) return null;
					var ret = await (Task<string>) method.Invoke(instance, args);
					return ret == null ? null : new Response {StatusCode = 200, Body = ret};
				};
			else
				throw new ArgumentException();
		}
	}
}