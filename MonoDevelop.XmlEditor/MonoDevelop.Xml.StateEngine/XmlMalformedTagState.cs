// 
// XmlMalformedTagState.cs
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

using System;

namespace MonoDevelop.Xml.StateEngine
{
	
	public class XmlMalformedTagState : State
	{
		
		public XmlMalformedTagState (State parent, int position)
			: base (parent, position)
		{
		}
		
		protected XmlMalformedTagState (XmlMalformedTagState copyFrom, bool copyParents)
			: base (copyFrom, copyParents)
		{
		}

		public override State PushChar (char c, int position)
		{
			if (c == '<' || c == '>') {
				Close (position);
				return this.Parent;
			}
				
			return null;
		}

		public override string ToString ()
		{
			return "[XmlMalformedTag]";
		}
		
		public override State DeepCopy (bool copyParents)
		{
			return new XmlMalformedTagState (this, copyParents);
		}

	}
}
