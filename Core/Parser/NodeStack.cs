// 
// Parser.cs
// 
// Author:
//   Mikayla Hutchinson <m.j.hutchinson@gmail.com>
// 
// Copyright (C) 2008 Novell, Inc (http://www.novell.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System.Collections.Generic;
using MonoDevelop.Xml.Dom;

namespace MonoDevelop.Xml.Parser
{
	public class NodeStack : Stack<XObject>
	{
		public NodeStack (IEnumerable<XObject> collection) : base (collection) { }
		public NodeStack () { }

		public NodeStack (int capacity) : base (capacity) { }

		public XObject? Peek (int down)
		{
			int i = 0;
			foreach (XObject o in this) {
				if (i == down)
					return o;
				i++;
			}
			return null;
		}

		public XDocument? GetRoot ()
		{
			XObject? last = null;
			foreach (XObject o in this)
				last = o;
			return last as XDocument;
		}

		internal NodeStack ShallowCopy ()
		{
			IEnumerable<XObject> CopyXObjects ()
			{
				foreach (XObject o in this)
					yield return o.ShallowCopy ();
			}

			var copies = new List<XObject> (CopyXObjects ());
			copies.Reverse ();
			return new NodeStack (copies);
		}

		internal static NodeStack FromParents (XObject fromObject)
		{
			var newStack = new NodeStack ();

			DepthFirstAddParentsToStack (fromObject);

			void DepthFirstAddParentsToStack (XObject o)
			{
				if (o.Parent is XObject parent) {
					DepthFirstAddParentsToStack (parent);
					newStack.Push (parent.ShallowCopy ());
				}
			}

			return newStack;
		}
	}
}
