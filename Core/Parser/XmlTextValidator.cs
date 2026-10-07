// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Text;

using MonoDevelop.Xml.Analysis;
using MonoDevelop.Xml.Dom;

namespace MonoDevelop.Xml.Parser
{
	static class XmlTextValidator
	{
		public static void ValidateText (XmlParserContext context, StringBuilder text, int startPosition)
			=> Validate (context, text, startPosition, isAttributeValue: false);

		public static void ValidateAttributeValue (XmlParserContext context, StringBuilder value, int startPosition)
			=> Validate (context, value, startPosition, isAttributeValue: true);

		public static void ValidateCharacter (XmlParserContext context, char c)
		{
			if (context.Diagnostics is null || XmlChar.IsValid (c) || char.IsSurrogate (c)) {
				return;
			}

			context.Diagnostics.Add (XmlCoreDiagnostics.InvalidCharacter, new TextSpan (context.Position, 1), (int)c);
		}

		static void Validate (XmlParserContext context, StringBuilder text, int startPosition, bool isAttributeValue)
		{
			var diagnostics = context.Diagnostics;
			if (diagnostics is null) {
				return;
			}

			bool? entitiesMayBeDeclared = null;
			int length = text.Length;

			for (int i = 0; i < length; i++) {
				char c = text[i];

				if (c == '&') {
					i = ValidateReference (text, i, startPosition, diagnostics, context, ref entitiesMayBeDeclared) - 1;
					continue;
				}

				if (!isAttributeValue && c == ']' && i + 2 < length && text[i + 1] == ']' && text[i + 2] == '>') {
					diagnostics.Add (XmlCoreDiagnostics.CDataEndInText, new TextSpan (startPosition + i, 3));
					i += 2;
					continue;
				}

				if (char.IsHighSurrogate (c) && i + 1 < length && char.IsLowSurrogate (text[i + 1])) {
					i++;
					continue;
				}

				if (XmlChar.IsInvalid (c)) {
					diagnostics.Add (XmlCoreDiagnostics.InvalidCharacter, new TextSpan (startPosition + i, 1), (int)c);
				}
			}
		}

		static int ValidateReference (
			StringBuilder text,
			int start,
			int startPosition,
			List<XmlDiagnostic> diagnostics,
			XmlParserContext context,
			ref bool? entitiesMayBeDeclared)
		{
			int length = text.Length;
			int i = start + 1;

			if (i < length && text[i] == '#') {
				i++;
				bool isHex = i < length && text[i] == 'x';
				if (isHex) {
					i++;
				}

				int digitsStart = i;
				long value = 0;
				while (i < length && IsDigit (text[i], isHex)) {
					if (value <= 0x10FFFF) {
						value = value * (isHex ? 16 : 10) + HexValue (text[i]);
					}
					i++;
				}

				bool hasDigits = i > digitsStart;
				if (hasDigits && i < length && text[i] == ';') {
					i++;
					if (value > 0x10FFFF || XmlChar.IsInvalid ((int)value)) {
						diagnostics.Add (XmlCoreDiagnostics.InvalidCharacterReference, new TextSpan (startPosition + start, i - start), Slice (text, start, i));
					}
					return i;
				}

				if (hasDigits && (i >= length || !XmlChar.IsNameChar (text[i]))) {
					diagnostics.Add (XmlCoreDiagnostics.IncompleteEntityReference, new TextSpan (startPosition + start, i - start), Slice (text, start, i));
					return i;
				}

				while (i < length && XmlChar.IsNameChar (text[i])) {
					i++;
				}
				if (i < length && text[i] == ';') {
					i++;
				}

				diagnostics.Add (XmlCoreDiagnostics.InvalidCharacterReference, new TextSpan (startPosition + start, i - start), Slice (text, start, i));
				return i;
			}

			if (i < length && XmlChar.IsFirstNameChar (text[i])) {
				while (i < length && XmlChar.IsNameChar (text[i])) {
					i++;
				}

				if (i < length && text[i] == ';') {
					string name = Slice (text, start + 1, i);
					i++;
					if (XmlChar.GetPredefinedEntity (name) < 0) {
						entitiesMayBeDeclared ??= HasDocType (context);
						if (!entitiesMayBeDeclared.Value) {
							diagnostics.Add (XmlCoreDiagnostics.UndeclaredEntity, new TextSpan (startPosition + start, i - start), name);
						}
					}
					return i;
				}

				diagnostics.Add (XmlCoreDiagnostics.IncompleteEntityReference, new TextSpan (startPosition + start, i - start), Slice (text, start, i));
				return i;
			}

			diagnostics.Add (XmlCoreDiagnostics.UnescapedAmpersand, new TextSpan (startPosition + start, 1));
			return start + 1;
		}

		static bool HasDocType (XmlParserContext context)
		{
			XDocument? document = null;
			foreach (var node in context.Nodes) {
				if (node is XDocument d) {
					document = d;
				}
			}

			if (document is null) {
				return true;
			}

			foreach (var node in document.Nodes) {
				if (node is XDocType) {
					return true;
				}
			}

			return false;
		}

		static bool IsDigit (char c, bool isHex)
			=> (c >= '0' && c <= '9') || (isHex && ((c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')));

		static int HexValue (char c)
			=> c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

		static string Slice (StringBuilder text, int start, int end)
			=> text.ToString (start, end - start);
	}
}
