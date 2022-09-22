// 
// XmlAttributeValueState.cs
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
using MonoDevelop.Xml.Dom;

namespace MonoDevelop.Xml.Parser
{
	public class XmlAttributeValueState : XmlParserState
	{
		const int FREE = 0;
		const int UNQUOTED = 1;
		const int SINGLEQUOTE = 2;
		const int DOUBLEQUOTE = 3;

		//derived classes should use these if they need to store info in the tag
		protected const int TagMask = 3;
		protected const int TagShift = 2;

		public override XmlParserState? PushChar (char c, XmlParserContext context, ref string? rollback)
		{
			System.Diagnostics.Debug.Assert (((XAttribute) context.Nodes.Peek ()).Value == null);

			if (c == '<') {
				//the parent state should report the error
				rollback = string.Empty;
				return Parent;
			}

			if (context.CurrentStateLength == 0) {
				if (c == '"') {
					context.StateTag = DOUBLEQUOTE;
					return null;
				}
				if (c == '\'') {
					context.StateTag = SINGLEQUOTE;
					return null;
				}
				context.StateTag = UNQUOTED;
			}

			int maskedTag = context.StateTag & TagMask;

			if (maskedTag == UNQUOTED) {
				return BuildUnquotedValue (c, context, ref rollback);
			}

			if ((c == '"' && maskedTag == DOUBLEQUOTE) || c == '\'' && maskedTag == SINGLEQUOTE) {
				//ending the value
				var att = (XAttribute) context.Nodes.Peek ();
				att.Value = context.KeywordBuilder.ToString ();
				return Parent;
			}

			context.KeywordBuilder.Append (c);
			return null;
		}

		XmlParserState? BuildUnquotedValue (char c, XmlParserContext context, ref string? rollback)
		{
			if (char.IsLetterOrDigit (c) || c == '_' || c == '.') {
				context.KeywordBuilder.Append (c);
				return null;
			}

			if (context.KeywordBuilder.Length == 0) {
				context.Diagnostics?.LogError ($"The value of attribute '{(string)((XAttribute)context.Nodes.Peek ()).Name.FullName}' ended unexpectedly.", context.Position);
				rollback = string.Empty;
				return Parent;
			}

			var att = (XAttribute)context.Nodes.Peek ();
			att.Value = context.KeywordBuilder.ToString ();
			rollback = string.Empty;
			return Parent;
		}

		public override XmlParserContext TryRecreateState (XObject xobject, int position)
			=> throw new InvalidOperationException ("State has no corresponding XObject");

		public static char? GetDelimiterChar (XmlParserContext context)
			=> context.CurrentState is XmlAttributeValueState
			? (context.StateTag & TagMask) switch {
				SINGLEQUOTE => '\'',
				DOUBLEQUOTE => '"',
				_ => (char?) null
			}
			: null;
	}
}
