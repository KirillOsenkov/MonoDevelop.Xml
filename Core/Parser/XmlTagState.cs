// 
// XmlTagState.cs
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
using System.Diagnostics;
using System.Text;

using MonoDevelop.Xml.Analysis;
using MonoDevelop.Xml.Dom;

namespace MonoDevelop.Xml.Parser
{
	public class XmlTagState : XmlParserState
	{
		const int STARTOFFSET = 1; // <

		const int ATTEMPT_RECOVERY = 1;
		const int RECOVERY_FOUND_WHITESPACE = 2;
		const int MAYBE_SELF_CLOSING = 2;
		const int FREE = 0;

		readonly XmlAttributeState AttributeState;
		readonly XmlNameState NameState;
		
		public XmlTagState () : this (new XmlAttributeState ()) {}
		
		public XmlTagState  (XmlAttributeState attributeState)
			: this (attributeState, new XmlNameState ()) {}
		
		public XmlTagState (XmlAttributeState attributeState, XmlNameState nameState)
		{
			AttributeState = attributeState;
			NameState = nameState;
			
			Adopt (AttributeState);
			Adopt (NameState);
		}
		
		public override XmlParserState? PushChar (char c, XmlParserContext context, ref string? rollback)
		{
			var peekedNode = (XContainer) context.Nodes.Peek ();
			var element = peekedNode as XElement;

			// if the current node on the stack is ended or not an element, then it's the parent
			// and we need to create the new element
			if (element is null || element.IsEnded) {
				var parent = peekedNode;
				element = new XElement (context.Position - STARTOFFSET) { Parent = parent };
				context.Nodes.Push (element);
				if (context.BuildTree) {
					parent.AddChildNode (element);
				}
			}
			
			if (c == '<') {
				if (element.Name.IsValid) {
					context.Diagnostics?.Add (XmlCoreDiagnostics.MalformedNamedTag, context.Position, element.Name.FullName, '<');
					Close (element, context, context.Position);
				} else {
					context.Diagnostics?.Add (XmlCoreDiagnostics.MalformedTag, context.Position, '<');
				}
				
				rollback = string.Empty;
				return Parent;
			}
			
			Debug.Assert (!element.IsEnded);
			
			if (element.IsClosed && c != '>') {
				if (element.IsNamed) {
					context.Diagnostics?.Add (XmlCoreDiagnostics.MalformedNamedSelfClosingTag, context.Position, c);
				} else {
					context.Diagnostics?.Add (XmlCoreDiagnostics.MalformedSelfClosingTag, context.Position, c);
				}
				if (char.IsWhiteSpace (c)) {
					return null;
				}
				context.Nodes.Pop ();
				return Parent;
			}
			
			//if tag closed
			if (c == '>') {
				element.HasEndBracket = true;
				if (context.StateTag == MAYBE_SELF_CLOSING) {
					element.Close (element);
				}
				if (!element.IsNamed) {
					context.Diagnostics?.Add (XmlCoreDiagnostics.UnnamedTag, context.Position);
					element.End (context.Position + 1);
				} else {
					Close (element, context, context.Position + 1);
				}
				return Parent;
			}

			if (c == '/') {
				context.StateTag = MAYBE_SELF_CLOSING;
				return null;
			}

			if (context.StateTag == ATTEMPT_RECOVERY) {
				if (XmlChar.IsWhitespace (c)) {
					context.StateTag = RECOVERY_FOUND_WHITESPACE;
				}
				return null;
			}

			if (context.StateTag == RECOVERY_FOUND_WHITESPACE) {
				if (!XmlChar.IsFirstNameChar (c))
					return null;
			}

			context.StateTag = FREE;

			if (context.CurrentStateLength > 0 && XmlChar.IsFirstNameChar (c)) {
				rollback = string.Empty;
				return AttributeState;
			}

			if (!element.IsNamed && XmlChar.IsFirstNameChar (c)) {
				rollback = string.Empty;
				return NameState;
			}

			if (XmlChar.IsWhitespace (c))
				return null;

			// namestate will have reported an error already
			if (context.PreviousState is XmlNameState) {
				context.Diagnostics?.Add (XmlCoreDiagnostics.MalformedTag, context.Position, c);
			}

			context.StateTag = ATTEMPT_RECOVERY;
			return null;
		}
		
		protected virtual void Close (XElement element, XmlParserContext context, int endOffset)
		{
			//have already checked that element is not null, i.e. top of stack is our element
			if (element.IsClosed)
				context.Nodes.Pop ();

			element.End (endOffset);
		}

		public override XmlParserContext? TryRecreateState (XObject xobject, int position)
		{
			var fromAtt = AttributeState.TryRecreateState (xobject, position);
			if (fromAtt != null) {
				return fromAtt;
			}

			// we can also recreate state for attributes within the tag, if the attribute state didn't
			var el = xobject as XElement;
			if (el == null && xobject is XAttribute a) {
				el = (XElement?) a.Parent;
			}

			if (el != null && position >= el.Span.Start && position < el.Span.End - 1) {
				// recreating name builder and value builder state is a pain to get right
				// for now, let parent recreate state at start of tag
				if (position <= el.NameSpan.End) {
					return null;
				}

				// if there are attributes, then at the start of an attribute is also a pretty safe place to recreate state
				// but if not, let parent recreate state at start of tag
				if (el.Attributes.First == null) {
					return null;
				}

				var newEl = new XElement (el.Span.Start, el.Name);

				int foundPosition = position;
				int prevStateEnd = el.NameSpan.End;
				XmlParserState prevState = NameState;

				foreach (var att in el.Attributes) {
					if (att.Span.End < position) {
						prevStateEnd = att.Span.End;
						prevState = AttributeState;
						//spine parser is currently expected to have attributes
						newEl.Attributes.AddAttribute ((XAttribute)att.ShallowCopy ());
						continue;
					}
					if (att.Span.End > position) {
						foundPosition = Math.Min (position, att.Span.Start);
						break;
					}
				}

				var parents = NodeStack.FromParents (el);
				parents.Push (newEl);

				return new XmlParserContext (
					currentState: this,
					position: foundPosition,
					previousState: prevState,
					currentStateLength: foundPosition - prevStateEnd,
					keywordBuilder: new StringBuilder (),
					nodes: parents,
					stateTag: FREE
				);
			}

			return null;
		}

		public static bool IsFree (XmlParserContext context) => context.CurrentState is XmlTagState && context.StateTag == FREE;
	}
}
