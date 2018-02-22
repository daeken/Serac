using System;

namespace Serac.Katatonic {
	[AttributeUsage(AttributeTargets.Class)]
	public class HandlerAttribute : Attribute {
		public readonly string Path;
		public HandlerAttribute(string path = "/") => Path = path;
	}

	public abstract class MethodAttribute : Attribute {
		public readonly string Name;
		protected MethodAttribute(string name = null) => Name = name;
	}
	[AttributeUsage(AttributeTargets.Method)]
	public class GetAttribute : MethodAttribute {
		public GetAttribute(string name = null) : base(name) {}
	}
	[AttributeUsage(AttributeTargets.Method)]
	public class PostAttribute : MethodAttribute {
		public PostAttribute(string name = null) : base(name) {}
	}
	[AttributeUsage(AttributeTargets.Method)]
	public class PutAttribute : MethodAttribute {
		public PutAttribute(string name = null) : base(name) {}
	}
	[AttributeUsage(AttributeTargets.Method)]
	public class DeleteAttribute : MethodAttribute {
		public DeleteAttribute(string name = null) : base(name) {}
	}

	[AttributeUsage(AttributeTargets.Method)]
	public class KatatonicArgumentParserAttribute : Attribute {
		public readonly Type Type;
		public KatatonicArgumentParserAttribute(Type type) => Type = type;
	}

	[AttributeUsage(AttributeTargets.Method)]
	public class KatatonicHandlerBuilderAttribute : Attribute {
		public readonly Type Type;
		public KatatonicHandlerBuilderAttribute(Type type) => Type = type;
	}
}