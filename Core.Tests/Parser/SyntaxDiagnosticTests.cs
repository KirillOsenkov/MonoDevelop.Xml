// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MonoDevelop.Xml.Analysis;
using MonoDevelop.Xml.Parser;

using NUnit.Framework;

namespace MonoDevelop.Xml.Tests.Parser
{
	[TestFixture]
	public class SyntaxDiagnosticTests
	{
		[TestCase ("<a>x & y</a>", nameof (XmlCoreDiagnostics.UnescapedAmpersand), 5, 1)]
		[TestCase ("<a>&;</a>", nameof (XmlCoreDiagnostics.UnescapedAmpersand), 3, 1)]
		[TestCase ("<a b=\"x & y\"/>", nameof (XmlCoreDiagnostics.UnescapedAmpersand), 8, 1)]
		[TestCase ("<a>&lt</a>", nameof (XmlCoreDiagnostics.IncompleteEntityReference), 3, 3)]
		[TestCase ("<a>&lt x</a>", nameof (XmlCoreDiagnostics.IncompleteEntityReference), 3, 3)]
		[TestCase ("<a>&#65 x</a>", nameof (XmlCoreDiagnostics.IncompleteEntityReference), 3, 4)]
		[TestCase ("<a>&#xZZ;</a>", nameof (XmlCoreDiagnostics.InvalidCharacterReference), 3, 6)]
		[TestCase ("<a>&#;</a>", nameof (XmlCoreDiagnostics.InvalidCharacterReference), 3, 3)]
		[TestCase ("<a>&#0;</a>", nameof (XmlCoreDiagnostics.InvalidCharacterReference), 3, 4)]
		[TestCase ("<a>&#xD800;</a>", nameof (XmlCoreDiagnostics.InvalidCharacterReference), 3, 8)]
		[TestCase ("<a>&#x110000;</a>", nameof (XmlCoreDiagnostics.InvalidCharacterReference), 3, 10)]
		[TestCase ("<a>&#99999999999999999999;</a>", nameof (XmlCoreDiagnostics.InvalidCharacterReference), 3, 23)]
		[TestCase ("<a>&foo;</a>", nameof (XmlCoreDiagnostics.UndeclaredEntity), 3, 5)]
		[TestCase ("<a b='&foo;'/>", nameof (XmlCoreDiagnostics.UndeclaredEntity), 6, 5)]
		[TestCase ("<a>x ]]> y</a>", nameof (XmlCoreDiagnostics.CDataEndInText), 5, 3)]
		[TestCase ("<a>x \u0001 y</a>", nameof (XmlCoreDiagnostics.InvalidCharacter), 5, 1)]
		[TestCase ("<a b=\"\u0001\"/>", nameof (XmlCoreDiagnostics.InvalidCharacter), 6, 1)]
		[TestCase ("<a><!-- \xFFFF --></a>", nameof (XmlCoreDiagnostics.InvalidCharacter), 8, 1)]
		[TestCase ("<a><![CDATA[\u0002]]></a>", nameof (XmlCoreDiagnostics.InvalidCharacter), 12, 1)]
		[TestCase ("<a b=\"1\"c=\"2\"/>", nameof (XmlCoreDiagnostics.MissingWhitespaceBetweenAttributes), 8, 1)]
		[TestCase ("<a b=\"1\" / >", nameof (XmlCoreDiagnostics.MalformedNamedSelfClosingTag), 10, 1)]
		[TestCase ("<a></ a>", nameof (XmlCoreDiagnostics.WhitespaceBeforeClosingTagName), 5, 1)]
		[TestCase ("<a><!-- x ---></a>", nameof (XmlCoreDiagnostics.IncompleteEndComment), 12, 1)]
		[TestCase (" <?xml version=\"1.0\"?><a/>", nameof (XmlCoreDiagnostics.MisplacedXmlDeclaration), 1, 5)]
		[TestCase ("<a><?xml version=\"1.0\"?></a>", nameof (XmlCoreDiagnostics.MisplacedXmlDeclaration), 3, 5)]
		[TestCase ("<a/><!DOCTYPE a>", nameof (XmlCoreDiagnostics.MisplacedDocType), 4, 11)]
		[TestCase ("<!DOCTYPE a><!DOCTYPE a><a/>", nameof (XmlCoreDiagnostics.MisplacedDocType), 12, 11)]
		[TestCase ("<a/><b/>", nameof (XmlCoreDiagnostics.MultipleRootElements), 5, 1)]
		[TestCase ("text<a/>", nameof (XmlCoreDiagnostics.TextOutsideRootElement), 0, 4)]
		[TestCase ("<a/>text", nameof (XmlCoreDiagnostics.TextOutsideRootElement), 4, 4)]
		[TestCase ("<a/><![CDATA[x]]>", nameof (XmlCoreDiagnostics.CDataOutsideRootElement), 4, 13)]
		[TestCase ("<a>", nameof (XmlCoreDiagnostics.UnclosedTag), 0, 3)]
		public void ReportsDiagnostic (string document, string descriptorName, int start, int length)
		{
			var descriptor = (XmlDiagnosticDescriptor)typeof (XmlCoreDiagnostics).GetField (descriptorName)!.GetValue (null)!;

			var parser = new XmlTreeParser (new XmlRootState ());
			var result = parser.Parse (document);

			result.AssertDiagnostics ((descriptor, start, length));
		}

		[TestCase ("<a>&amp; &lt; &gt; &quot; &apos; &#65; &#x41; &#x1F600;</a>")]
		[TestCase ("<a b=\"&amp;&#65;\" c='\"' d=\"'\"/>")]
		[TestCase ("<a>]] > ]]</a>")]
		[TestCase ("<a b=\"]]>\"/>")]
		[TestCase ("<a>\xD83D\xDE00</a>")]
		[TestCase ("<a b=\"1\" c=\"2\"/>")]
		[TestCase ("<a b=\"1\"\r\n\tc=\"2\" />")]
		[TestCase ("<a></a >")]
		[TestCase ("<?xml version=\"1.0\"?>\n<?xml-stylesheet href=\"x\"?><a/>")]
		[TestCase ("<!-- c --><?pi?><a/><!-- c -->\n")]
		[TestCase ("<!DOCTYPE a [<!ENTITY foo \"b>r\">]><a>&foo;</a>")]
		[TestCase ("<!DOCTYPE a SYSTEM 'a.dtd'><a>&foo;</a>")]
		[TestCase ("<!DOCTYPE a PUBLIC \"-//x\" \"a.dtd\" [<!ENTITY foo 'bar'>]><a>&foo;</a>")]
		[TestCase ("<a><!-- x - y --></a>")]
		public void ValidDocumentHasNoDiagnostics (string document)
		{
			var parser = new XmlTreeParser (new XmlRootState ());
			var result = parser.Parse (document);

			result.AssertNoDiagnostics ();
		}

		[Test]
		public void LoneSurrogateIsInvalidCharacter ()
		{
			var parser = new XmlTreeParser (new XmlRootState ());
			var result = parser.Parse ("<a>" + (char)0xD800 + "</a>");

			result.AssertDiagnostics ((XmlCoreDiagnostics.InvalidCharacter, 3, 1));
		}

		[Test]
		public void MalformedTagDoesNotCascadeIntoTextOutsideRoot ()
		{
			var parser = new XmlTreeParser (new XmlRootState ());
			var result = parser.Parse ("<1a/>");

			result.AssertDiagnostics ((XmlCoreDiagnostics.MalformedTagOpening, 0, 1));
		}
	}
}
