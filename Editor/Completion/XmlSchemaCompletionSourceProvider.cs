using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using MonoDevelop.Xml.Editor.Logging;
using MonoDevelop.Xml.Editor.Parsing;

namespace MonoDevelop.Xml.Editor.Completion
{
	[Export (typeof (IAsyncCompletionSourceProvider))]
	[ContentType (XmlContentTypeNames.XmlCore)]
	[Name ("XmlSchemaCompletionProvider")]
	class XmlSchemaCompletionSourceProvider : IAsyncCompletionSourceProvider
	{
		[Import (AllowDefault = true)]
		private IXmlSchemaService xmlSchemaService = null;

		[Import]
		private IEditorLoggerFactory loggerService = null;

		[Import]
		private XmlParserProvider xmlParserProvider = null;

		private string TryGetFilePath (ITextBuffer textBuffer)
		{
			if (textBuffer.Properties.TryGetProperty (typeof (ITextDocument), out ITextDocument textDocument)) {
				return textDocument.FilePath;
			}

			return null;
		}

		public IAsyncCompletionSource GetOrCreate (ITextView textView)
		{
			var filePath = TryGetFilePath (textView.TextBuffer);
			if (filePath == null) {
				return null;
			}

			var schema = xmlSchemaService?.TryGetXmlSchemaForFile (filePath);
			if (schema == null) {
				return null;
			}

			var logger = loggerService.CreateLogger<XmlSchemaCompletionSource>(textView);

			return new XmlSchemaCompletionSource (textView, schema, logger, xmlParserProvider);
		}
	}
}