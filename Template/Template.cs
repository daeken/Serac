using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Serac.Template {
	public class TemplateException : Exception {
		internal TemplateException(string message) : base(message) {}
	}
	
	public class Template<ModelT> {
		static int TemplateI;
		string TemplateClassName;
		readonly Func<ModelT, string> Compiled;

		abstract class Node {
			internal List<Node> Children = new List<Node>();
		}

		class RootNode : Node {
			public override string ToString() =>
				string.Join("\n", Children.Select(x => x.ToString()));
		}

		class TagNode : Node {
			internal string Name, Cls, Id;
			internal List<(string, string)> AttrStmts = new List<(string, string)>();
			internal List<string> AttrFuncs = new List<string>();

			public override string ToString() {
				var ret = $"- Tag {Name}";
				if(Cls != null)
					ret += $" .{Cls}";
				if(Id != null)
					ret += $" #{Id}";
				foreach(var child in Children)
					ret += $"\n{string.Join("\n", child.ToString().Split('\n').Select(x => $"\t{x}"))}";
				return ret;
			}
		}

		class BodyNode : Node {
			internal List<BodyValueNode> Values = new List<BodyValueNode>();
			
			public override string ToString() => $"- BodyNode {string.Join(", ", Values.Select(x => x.ToString()))}";
		}

		abstract class BodyValueNode {}

		class TextNode : BodyValueNode {
			internal string Value;
			
			public override string ToString() => $"'{Value}'";
		}

		class ExprNode : BodyValueNode {
			internal string Expr;
			
			public override string ToString() => $"expr('{Expr}')";
		}

		class CodeNode : Node {
			internal string Code;
			
			public override string ToString() =>
				$"- Codeblock {Code}{string.Join("", Children.Select(child => "\n" + string.Join("\n", child.ToString().Split('\n').Select(x => $"\t{x}"))))}";
		}

		static Node Simplify(Node node) {
			switch(node) {
				case BodyNode body:
					var bn = new BodyNode();
					for(var i = 0; i < body.Values.Count; ++i) {
						if(body.Values[i] is ExprNode) {
							bn.Values.Add(body.Values[i]);
							continue;
						}

						var tt = "";
						for(; i < body.Values.Count; ++i) {
							if(body.Values[i] is TextNode text)
								tt += text.Value;
							else
								break;
						}
						bn.Values.Add(new TextNode { Value = tt });
						i--;
					}
					return bn;
				case RootNode root:
					return new RootNode { Children = Simplify(root.Children) };
				case TagNode tag:
					return new TagNode {
						Name = tag.Name, Cls = tag.Cls, Id = tag.Id, 
						AttrFuncs = tag.AttrFuncs, AttrStmts = tag.AttrStmts, 
						Children = Simplify(tag.Children)
					};
				case CodeNode code:
					return new CodeNode {
						Code = code.Code, 
						Children = Simplify(code.Children)
					};
			}

			return null;
		}

		static List<Node> Simplify(List<Node> nodes) {
			var ret = new List<Node>();
			for(var i = 0; i < nodes.Count; ++i) {
				if(nodes[i] is BodyNode) {
					var bn = new BodyNode();
					for(; i < nodes.Count; ++i) {
						if(nodes[i] is BodyNode ibody) {
							if(bn.Values.Count != 0)
								bn.Values.Add(new TextNode { Value="\n" });
							bn.Values = bn.Values.Concat(ibody.Values).ToList();
						} else
							break;
					}
					i--;
					ret.Add(bn);
				} else
					ret.Add(nodes[i]);
			}
			return ret.Select(Simplify).ToList();
		}
		
		public Template(string fn) {
			var code = File.ReadAllText(fn);
			var (topLevel, node) = Parse(code.Split('\n'));
			var csc = ToCode(node);
			csc = string.Join("", topLevel.Select(x => $"{x};\n")) + csc;
			var compiler = CSharpCompilation.Create(TemplateClassName)
				.WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
				.AddReferences(MetadataReference.CreateFromFile(typeof(ModelT).Assembly.Location))
				.AddSyntaxTrees(CSharpSyntaxTree.ParseText(csc));
			if(AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string asms)
				compiler = compiler.AddReferences(asms.Split(Path.PathSeparator).Select(x => MetadataReference.CreateFromFile(x)));
			var dllpath = Path.Combine(Path.GetTempPath(), TemplateClassName + ".dll");
			var res = compiler.Emit(dllpath);
			if(!res.Success)
				throw new TemplateException($"Compilation of template {fn} failed: {string.Join(" ; ", res.Diagnostics.Select(x => x.GetMessage()))}");

			var dll = AssemblyLoadContext.Default.LoadFromAssemblyPath(dllpath);
			var method = dll.GetType($"Serac.Template.AnonymousTemplates.{TemplateClassName}").GetMethod("Render");
			Compiled = (Func<ModelT, string>) method.CreateDelegate(typeof(Func<ModelT, string>));
		}

		string ToCode(Node node, int indent=0) {
			switch(node) {
				case RootNode root:
					var rret = "using System.Text;\n";
					rret += "using Serac.Template;\n";
					rret += $"namespace Serac.Template.AnonymousTemplates {{\n";
					TemplateClassName = $"Template{TemplateI++}";
					rret += $"public static class {TemplateClassName} {{\n";
					rret += $"public static string Render({typeof(ModelT).FullName} _obj) {{\n";
					rret += string.Join("", typeof(ModelT).GetMembers()
						.Where(x => x.MemberType == MemberTypes.Field || x.MemberType == MemberTypes.Property)
						.Select(x => $"var {x.Name} = _obj.{x.Name};\n")
					);
					rret += $"var sb = new StringBuilder();\n{string.Join("\n", root.Children.Select(x => ToCode(x, indent+1)))}\nreturn sb.ToString();\n}}\n}}\n}}";
					return rret;
				case TagNode tag:
					var ret = $"sb.Append($\"{new string('\t', indent)}<{tag.Name.SanitizeInterp()}";
					if(tag.Id != null)
						ret += $" id=\\\"{tag.Id.SanitizeInterp()}\\\"";
					if(tag.Cls != null)
						ret += $" class=\\\"{tag.Cls.SanitizeInterp()}\\\"";
					foreach(var (k, v) in tag.AttrStmts)
						ret += $" {k.Replace(">", "_").Replace("\"", "_")}=\\\"{{({v}).HtmlEncode()}}\\\"";
					foreach(var (i, o) in tag.AttrFuncs.Enumerate()) {
						if(i == 0)
							ret += "\");\n";
						ret += $"foreach(var (_k, _v) in {o})\n";
						ret += "\tsb.Append($\" {_k.Replace(\">\", \"_\").Replace(\"\\\"\", \"_\")}=\\\"{_v.HtmlEncode()}\\\"\")";
						if(i == tag.AttrFuncs.Count - 1)
							ret += "sb.Append($\"";
					}
					ret += ">\\n\");\n";
					foreach(var child in tag.Children)
						ret += $"{ToCode(child, tag.Name == "script" ? 0 : indent+1)}\n";
					ret += $"sb.Append(\"{new string('\t', indent)}</{tag.Name.SanitizeQuote()}>\\n\");";
					return ret;
				case BodyNode body:
					return $"sb.Append(\"{new string('\t', indent)}\");\n" + string.Join("\n", body.Values.Select(x => {
						switch(x) {
							case TextNode text:
								return $"@\"{text.Value.SanitizeVerbatim()}\"";
							case ExprNode expr:
								return $"((object) ({expr.Expr}))?.ToString() ?? \"\"";
						}

						return null;
					}).Select(x => $"sb.Append({x});")) + "\nsb.Append(\"\\n\");";
				case CodeNode code:
					return $"{code.Code} {{\n{string.Join("\n", code.Children.Select(x => ToCode(x, indent+1)))}\n}}";
				default:
					return null;
			}
		}

		static (List<string>, Node) Parse(IEnumerable<string> lines) {
			List<BodyValueNode> ParseTextBody(string body) {
				var elems = new List<BodyValueNode>();
				var ppos = 0;
				while(true) {
					var spos = body.IndexOf('{', ppos);
					if(spos == -1) {
						if(ppos != body.Length - 1)
							elems.Add(new TextNode { Value=body.Substring(ppos) });
						break;
					}
					if(spos != ppos)
						elems.Add(new TextNode { Value=body.Substring(ppos, spos - ppos) });
					var epos = body.CSIndexOf("}", spos + 1);
					elems.Add(new ExprNode { Expr=body.Substring(spos + 1, epos - spos - 1).Trim() });
					ppos = epos + 1;
				}
				return elems;
			}
			
			var nodes = new List<Node> { new RootNode() };
			var parentIndent = 0;
			var topLevel = new List<string>();
			
			foreach(var fline in lines) {
				if(fline.Trim().Length == 0)
					continue;

				var curIndent = 0;
				foreach(var c in fline)
					if(c == '\t') curIndent++;
					else break;
				
				var parent = nodes.Last();
				if(curIndent < parentIndent) {
					nodes = nodes.Take(curIndent).ToList();
					parentIndent = curIndent;
				} else if(curIndent > parentIndent && !(parent is TagNode pitag && pitag.Name == "script")) {
					if(curIndent != parentIndent + 1)
						throw new TemplateException("Indentation mismatch");
					nodes.Add(parent.Children.Last());
					parentIndent = curIndent;
				}
				parent = nodes.Last();
				var line = fline.Substring(parentIndent);

				if(parent is TagNode ptag && ptag.Name == "script") {
					var bn = new BodyNode();
					bn.Values.Add(new TextNode { Value=line });
					parent.Children.Add(bn);
					continue;
				}

				switch(line[0]) {
					case '~':
						topLevel.Add(line.Substring(1));
						break;
					case '-': case '.': case '#':
						var elems = Regex.Match(line, @"^(-\s*[a-zA-Z0-9_-]+|\.[a-zA-Z0-9_-]+|#[a-zA-Z0-9_-]+)+\s*(.*)$");
						if(!elems.Success) {
							parent.Children.Add(new BodyNode { Values=ParseTextBody(line) });
							break;
						}

						string tag = "div", id = null, cls = null;
						var mods = elems.Groups[1].Captures;
						foreach(var _mod in mods) {
							var mod = ((Capture) _mod).Value;
							var mc = mod[0];
							var name = mod.Substring(1).Trim();
							if(mc == '-') tag = name;
							else if(mc == '.') cls = name;
							else if(mc == '#') id = name;
						}

						var tn = new TagNode { Name=tag, Id=id, Cls=cls };
						
						line = elems.Groups[2].Value;
						if(line.Length > 1 && line[0] == '(') {
							line = line.Substring(1);
							while(line[0] != ')') {
								var (arg, rest) = line.GetUntil(",)");
								if(rest == null)
									throw new TemplateException("Missing end parenthesis for tag attribute group");
								var match = Regex.Match(arg, @"^([a-zA-Z0-9_-]+)=(.*)$");
								if(match.Success)
									tn.AttrStmts.Add((match.Groups[1].Value, match.Groups[2].Value));
								else
									tn.AttrFuncs.Add(arg);
								line = rest.Trim();
							}
							line = line.Substring(1).Trim();
						}

						if(line.Length != 0 && line[0] == '=') {
							if(tag == "script")
								throw new TemplateException("Variable body for script tag not allowed");
							var bn = new BodyNode();
							bn.Values.Add(new ExprNode { Expr=line.Substring(1).Trim() });
							tn.Children.Add(bn);
						}

						parent.Children.Add(tn);
						break;
					case '@':
						parent.Children.Add(new CodeNode { Code=line.Substring(1).Trim() });
						break;
					default:
						parent.Children.Add(new BodyNode { Values=ParseTextBody(line) });
						break;
				}
			}

			return (topLevel, Simplify(nodes.First()));
		}

		public string Render(ModelT model) => Compiled(model);
	}
}