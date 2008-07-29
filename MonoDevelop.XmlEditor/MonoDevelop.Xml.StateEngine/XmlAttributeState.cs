// 
// XmlAttributeState.cs
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
using System.Text;
using System.Diagnostics;

namespace MonoDevelop.Xml.StateEngine
{
	
	
	public class XmlAttributeState : State
	{
		XmlNameState XmlNameState;
		XmlAttributeValueState XmlAttributeValueState;
		
		const int NAMING = 0;
		const int GETTINGEQ = 1;
		const int GETTINGVAL = 2;
		
		public XmlAttributeState ()
			: this (new XmlNameState (), new XmlAttributeValueState ())
		{}
		
		public XmlAttributeState (XmlNameState nameState, XmlAttributeValueState valueState)
		{
			this.XmlNameState = nameState;
			this.XmlAttributeValueState = valueState;
			Adopt (this.XmlNameState);
			Adopt (this.XmlAttributeValueState);
		}

		public override State PushChar (char c, IParseContext context, ref bool reject)
		{
			XAttribute att = context.Nodes.Peek () as XAttribute;
			
			if (c == '<') {
				context.LogError ("Attribute ended unexpectedly with '<' character.");
				if (att != null)
					context.Nodes.Pop ();
				reject = true;
				return this.Parent;
			}
			
			//state has just been entered
			if (context.CurrentStateLength == 1)  {
				
				//starting a new attribute?
				if (att == null) {
					Debug.Assert (context.StateTag == NAMING);
					att = new XAttribute (context.Position);
					context.Nodes.Push (att);
					reject = true;
					return XmlNameState;
				} else {
					Debug.Assert (att.IsNamed);
					if (att.Value == null) {
						context.StateTag = GETTINGEQ;
					} else {
						//Got value, so end attribute
						context.Nodes.Pop ();
						att.End (context.Position);
						IAttributedXObject element = (IAttributedXObject) context.Nodes.Peek ();
						element.Attributes.AddAttribute (att);
						reject = true;
						return Parent;
					}
				}
			}
			
			if (c == '>') {
				context.LogWarning ("Attribute ended unexpectedly with '>' character.");
				if (att != null)
					context.Nodes.Pop ();
				reject = true;
				return this.Parent;
			}
			
			if (context.StateTag == GETTINGEQ) {
				if (char.IsWhiteSpace (c)) {
					return null;
				} else if (c == '=') {
					context.StateTag = GETTINGVAL;
					return null;
				}
			} else if (context.StateTag == GETTINGVAL) {
				if (char.IsWhiteSpace (c)) {
					return null;
				} else if (char.IsLetterOrDigit (c) || c== '\'' || c== '"') {
					reject = true;
					return XmlAttributeValueState;
				}
			}
			
			context.LogError ("Unexpected character '" + c + "' in attribute.");
			if (att != null)
				context.Nodes.Pop ();
			reject = true;
			return Parent;
		}
	}
}
