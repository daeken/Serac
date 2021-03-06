﻿using Serac.Template;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using static System.Console;

namespace Serac.Katatonic {
	public class NonexistentArgument {
		public static readonly NonexistentArgument Instance = new NonexistentArgument();
	}

	public static class KatatonicHelpers {
		[KatatonicHandlerBuilder(typeof(Task))]
		public static Func<Request, object[], MethodInfo, object, Task<Response>> EmptyResponseBuilder() => 
			async (request, args, method, instance) => {
				if(args == null) return null;
				await (Task) method.Invoke(instance, args);
				return new Response {StatusCode = 200};
			};
		
		[KatatonicHandlerBuilder(typeof(Task<Response>))]
		public static Func<Request, object[], MethodInfo, object, Task<Response>> ResponseBuilder() =>
			async (request, args, method, instance) => {
				if(args == null) return null;
				return await (Task<Response>) method.Invoke(instance, args);
			};

		[KatatonicHandlerBuilder(typeof((string, string)))]
		public static Func<Request, object[], MethodInfo, object, Task<Response>> SyncStringResponseBuilder() =>
			(request, args, method, instance) => {
				if(args == null) return null;
				try {
					var (mime, data) = ((string, string)) method.Invoke(instance, args);
					return Task.FromResult(mime == null ? null : new Response {StatusCode = 200, Body = data, ContentType = mime});
				} catch(Exception e) {
					WriteLine($"Exception: {e.StackTrace}\n{e}");
					throw;
				}
			};

		[KatatonicHandlerBuilder(typeof(Task<(string, string)>))]
		public static Func<Request, object[], MethodInfo, object, Task<Response>> StringResponseBuilder() =>
			async (request, args, method, instance) => {
				if(args == null) return null;
				var (mime, data) = await (Task<(string, string)>) method.Invoke(instance, args);
				return mime == null ? null : new Response {StatusCode = 200, Body = data, ContentType = mime};
			};

		[KatatonicArgumentParser(typeof(int))]
		public static Func<Request, object> IntParam(string rmethod, string name, bool hasDefault, int? def) {
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
		
		[KatatonicArgumentParser(typeof(string))]
		public static Func<Request, object> StringParam(string rmethod, string name, bool hasDefault, string def) {
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

		[KatatonicArgumentParser(typeof(bool))]
		public static Func<Request, object> BoolParam(string rmethod, string name, bool hasDefault, bool? def) {
			if(rmethod == "POST") {
				if(hasDefault)
					return request => request.PostParameters.ContainsKey(name)
						? (bool.TryParse(request.PostParameters[name], out var val) ? val : def)
						: def;
				else
					return request =>
						request.PostParameters.ContainsKey(name) && bool.TryParse(request.PostParameters[name], out var val)
							? (object) val
							: NonexistentArgument.Instance;
			} else {
				if(hasDefault)
					return request => request.Query.ContainsKey(name)
						? (bool.TryParse(request.Query[name], out var val) ? val : def)
						: def;
				else
					return request =>
						request.Query.ContainsKey(name) && bool.TryParse(request.Query[name], out var val)
							? (object) val
							: NonexistentArgument.Instance;
			}
		}
	}
	
	public class App {
		static readonly Dictionary<Type, List<Func<string, string, bool, object, Func<Request, object>>>> ArgParsers =
			new Dictionary<Type, List<Func<string, string, bool, object, Func<Request, object>>>>();
		static readonly Dictionary<Type, Func<Func<Request, object[], MethodInfo, object, Task<Response>>>> HandlerBuilders = 
			new Dictionary<Type, Func<Func<Request, object[], MethodInfo, object, Task<Response>>>>();

		static App() {
			foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
				foreach(var type in assembly.DefinedTypes)
					foreach(var meth in type.DeclaredMethods) {
						if(!meth.IsStatic) continue;
						var attr = (KatatonicArgumentParserAttribute) meth.GetCustomAttribute(typeof(KatatonicArgumentParserAttribute));
						if(attr != null) {
							var atype = attr.Type;
							if(!ArgParsers.ContainsKey(atype))
								ArgParsers[atype] = new List<Func<string, string, bool, object, Func<Request, object>>>();

							Func<string, string, bool, object, Func<Request, object>> BuildTrampoline(MethodInfo methi) =>
								(rmethod, name, hasDefault, defobj) =>
									(Func<Request, object>) methi.Invoke(null, new[] {rmethod, name, hasDefault, hasDefault ? defobj : null});

							ArgParsers[atype].Add(BuildTrampoline(meth));
							continue;
						}

						var hattr = (KatatonicHandlerBuilderAttribute) meth.GetCustomAttribute(typeof(KatatonicHandlerBuilderAttribute));
						if(hattr != null) {
							Func<Func<Request, object[], MethodInfo, object, Task<Response>>> BuildTrampoline(MethodInfo methi) =>
								() => (Func<Request, object[], MethodInfo, object, Task<Response>>) methi.Invoke(null, null);
							HandlerBuilders[hattr.Type] = BuildTrampoline(meth);
						}
					}
		}
		
		public readonly Dictionary<(string Method, string Path), Func<Request, Task<Response>>> Handlers = 
			new Dictionary<(string Method, string Path), Func<Request, Task<Response>>>();
		
		internal App(Action<App> builder) => builder(this);

		internal async Task<Response> Handle(Request request) {
			var key = (request.Method, request.Path);
			return Handlers.ContainsKey(key) ? await Handlers[key](request) : null;
		}

		public App Register<HandlerT>() where HandlerT : Katatonic, new() {
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
				Handlers[(method, tpath)] = BuildHandler<HandlerT>(method, meth);
			}
			
			return this;
		}

		Func<Request, Task<Response>> BuildHandler<HandlerT>(string rmethod, MethodInfo method) where HandlerT : Katatonic, new() {
			var argFuncs = new List<Func<Request, object>>();

			foreach(var arg in method.GetParameters()) {
				if(ArgParsers.ContainsKey(arg.ParameterType)) {
					var hit = false;
					foreach(var parser in ArgParsers[arg.ParameterType]) {
						var af = parser(rmethod, arg.Name, arg.HasDefaultValue, arg.DefaultValue);
						if(af == null) continue;
						argFuncs.Add(af);
						hit = true;
					}
					if(!hit)
						throw new ArgumentException();
				} else
					throw new ArgumentException();
			}
			
			object[] ArgBuilder(Request request) {
				var args = argFuncs.Select(x => x(request)).ToArray();
				return args.Contains(NonexistentArgument.Instance) ? null : args;
			}

			Func<Request, object[], MethodInfo, object, Task<Response>> handlerWrapper = null;

			var tpattr = method.GetCustomAttribute(typeof(TemplateAttribute));
			if(tpattr is TemplateAttribute tattr) {
				(Type, object) MakeTemplateObject(Type modelType) {
					var tplt = typeof(Template<object>).GetGenericTypeDefinition().MakeGenericType(modelType);
					return (tplt, Activator.CreateInstance(tplt, tattr.Path));
				}
				if(method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)) {
					var (tt, tpl) = MakeTemplateObject(method.ReturnType.GetGenericArguments()[0]);
					var rm = tt.GetMethod("Render");
					handlerWrapper = async (request, args, _method, instance) => {
						var task = (Task) _method.Invoke(instance, null);
						await task.ConfigureAwait(false);
						var tdata = task.GetType().GetProperty("Result").GetValue(task);
						if(tdata == null) return null;
						var rendered = (string) rm.Invoke(tpl, new[] { tdata });
						if(rendered == null) return null;
						return new Response { StatusCode = 200, ContentType = "text/html", Body = rendered };
					};
				} else {
					var (tt, tpl) = MakeTemplateObject(method.ReturnType);
					var rm = tt.GetMethod("Render");
					handlerWrapper = async (request, args, _method, instance) => {
						var tdata = _method.Invoke(instance, null);
						if(tdata == null) return null;
						var rendered = (string) rm.Invoke(tpl, new[] { tdata });
						if(rendered == null) return null;
						return new Response { StatusCode = 200, ContentType = "text/html", Body = rendered };
					};
				}
			}

			if(handlerWrapper == null && !HandlerBuilders.ContainsKey(method.ReturnType))
				throw new ArgumentException();
			if(handlerWrapper == null)
				handlerWrapper = HandlerBuilders[method.ReturnType]();
			return async request => {
				var session = new Session(request);
				var instance = new HandlerT { Request = request, Session = session };
				Response resp;
				try {
					resp = await handlerWrapper(request, ArgBuilder(request), method, instance);
				} catch(Exception e) {
					WriteLine($"Exception while handling {request.RealPath}");
					WriteLine(e.Message);
					WriteLine(e.StackTrace);
					return null;
				}

				if(resp == null) return null;
				session.SetCookie(resp);
				return resp;
			};
		}
	}
}