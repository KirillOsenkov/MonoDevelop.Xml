//
// MonoDevelop XML Editor
//
// Copyright (C) 2005 Matthew Ward
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Schema;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using MonoDevelop.Xml.Editor.Completion;

namespace MonoDevelop.Xml.Editor.Completion
{
	/// <summary>
	/// Holds the completion (intellisense) data for an xml schema.
	/// </summary>
	[Export (typeof (IAsyncCompletionSourceProvider))]
	[ContentType (XmlContentTypeNames.XmlCore)]
	[Name ("XmlSchemaCompletionProvider")]
	class XmlSchemaCompletionProvider : IAsyncCompletionSourceProvider
	{
		string namespaceUri = String.Empty;
		XmlSchema schema = null;
		string fileName = String.Empty;
		string baseUri = string.Empty;
		bool readOnly = false;
		bool loaded = false;

		static string testFilePath = "/Users/allisonkim/vsmac/main/external/MonoDevelop.Xml/Editor/Completion/";
		static string testFilename = "Microsoft.Build.xsd";

		static string testSnippetFilename = "snippetformat.xsd";
		static string testBuildFilename = "Microsoft.Build.xsd";

		/// <summary>
		/// Stores attributes that have been prohibited whilst the code
		/// generates the attribute completion data.
		/// </summary>
		XmlSchemaObjectCollection prohibitedAttributes = new XmlSchemaObjectCollection ();

		#region Constructors

		public XmlSchemaCompletionProvider () : this(String.Empty, testFilePath + testFilename)
		{
			// TODO: Handle when no schema is provided -> inferred schema?
			// use a default xsd for testing
		}

		/// <summary>
		/// Creates completion data from the schema passed in 
		/// via the reader object.
		/// </summary>
		public XmlSchemaCompletionProvider (TextReader reader)
		{
			this.schema = ReadSchema (String.Empty, reader);
		}

		/// <summary>
		/// Creates the completion data from the specified schema file.
		/// </summary>
		public XmlSchemaCompletionProvider (string fileName) : this (String.Empty, fileName)
		{
		}

		/// <summary>
		/// Creates the completion data from the specified schema file and uses
		/// the specified baseUri to resolve any referenced schemas.
		/// </summary>
		public XmlSchemaCompletionProvider (string baseUri, string fileName) : this (baseUri, fileName, false)
		{
		}

		//lazyLoadFile should not be used when the namespace property needs to be read
		public XmlSchemaCompletionProvider (string baseUri, string fileName, bool lazyLoadFile)
		{
			this.fileName = fileName;
			this.baseUri = baseUri;

			if (!lazyLoadFile)
				using (var reader = new StreamReader (fileName, true))
					this.schema = ReadSchema (baseUri, reader);
		}

		#endregion

		#region Provider

		public IAsyncCompletionSource GetOrCreate (ITextView textView)
		{
			return new XmlSchemaCompletionSource(textView, schema);
		}

		#endregion

		#region Properties

		public XmlSchema Schema {
			get {
				EnsureLoaded ();
				return schema;
			}
		}

		public bool ReadOnly {
			get { return readOnly; }
			set { readOnly = value; }
		}

		public string FileName {
			get { return fileName; }
			set { fileName = value; }
		}

		public string NamespaceUri {
			get { return namespaceUri; }
		}

		#endregion

		/// <summary>
		/// Converts the filename into a valid Uri.
		/// </summary>
		public static string GetUri (string fileName)
		{
			return string.IsNullOrEmpty (fileName) ? "" : new Uri (fileName).AbsoluteUri;
		}

		#region ILazilyLoadedProvider implementation

		public bool IsLoaded {
			get { return loaded; }
		}

		public void EnsureLoaded ()
		{
			EnsureLoadedAsync ().Wait ();
		}

		public Task EnsureLoadedAsync ()
		{
			if (loaded)
				return Task.CompletedTask;

			return Task.Run (() => {
				if (schema == null)
					using (var reader = new StreamReader (fileName, true))
						this.schema = ReadSchema (baseUri, reader);

				//TODO: should we evaluate unresolved imports against other registered schemas?
				//will be messy because we'll have to re-evaluate if any schema is added, removed or changes
				//maybe we should just force users to use schemaLocation in their includes
				var sset = new XmlSchemaSet {
					XmlResolver = new LocalOnlyXmlResolver ()
				};
				sset.Add (schema);
				sset.ValidationEventHandler += SchemaValidation;
				sset.Compile ();
				loaded = true;
			});
		}
		#endregion

		/// <summary>
		/// Finds the specified attribute in the schema. This method only checks
		/// the attributes defined in the root of the schema.
		/// </summary>
		public XmlSchemaAttribute FindAttribute (string name)
		{
			EnsureLoaded ();
			foreach (XmlSchemaAttribute attribute in schema.Attributes.Values)
				if (attribute.Name == name)
					return attribute;
			return null;
		}

		/// <summary>
		/// Finds the schema group with the specified name.
		/// </summary>
		public XmlSchemaGroup FindGroup (string name)
		{
			EnsureLoaded ();
			if (name != null) {
				foreach (XmlSchemaObject schemaObject in schema.Groups.Values) {
					var group = schemaObject as XmlSchemaGroup;
					if (group != null && group.Name == name)
						return group;
				}
			}
			return null;
		}

		/// <summary>
		/// Takes the name and creates a qualified name using the namespace of this
		/// schema.
		/// </summary>
		/// <remarks>If the name is of the form myprefix:mytype then the correct 
		/// namespace is determined from the prefix. If the name is not of this
		/// form then no prefix is added.</remarks>
		public QualifiedName CreateQualifiedName (string name)
		{
			int index = name.IndexOf (":");
			if (index >= 0) {
				string prefix = name.Substring (0, index);
				name = name.Substring (index + 1);
				EnsureLoaded ();
				//FIXME: look these up from the document's namespaces
				foreach (XmlQualifiedName xmlQualifiedName in schema.Namespaces.ToArray ()) {
					if (xmlQualifiedName.Name == prefix) {
						return new QualifiedName (name, xmlQualifiedName.Namespace, prefix);
					}
				}
			}

			// Default behaviour just return the name with the namespace uri.
			return new QualifiedName (name, namespaceUri);
		}

		/// <summary>
		/// Handler for schema validation errors.
		/// </summary>
		void SchemaValidation (object source, ValidationEventArgs e)
		{
			LoggingService.LogWarning ("Validation error loading schema '{0}': {1}", this.fileName, e.Message);
		}

		/// <summary>
		/// Loads the schema.
		/// </summary>
		XmlSchema ReadSchema (XmlReader reader)
		{
			try {
				var schema = XmlSchema.Read (reader, SchemaValidation);
				namespaceUri = schema.TargetNamespace;

				// is probably bad when there's nested includes due to recursive stack calls...
				foreach (XmlSchemaObject include in schema.Includes) {
					var includeSchema = include as XmlSchemaInclude;
					if (includeSchema != null) {
						includeSchema.Schema = ReadSchema (testFilePath + includeSchema.SchemaLocation, baseUri);
					}
				}
				return schema;
			} finally {
				reader.Close ();
			}
		}

		XmlSchema ReadSchema (string baseUri, TextReader reader)
		{
			// The default resolve can cause exceptions loading
			// xhtml1-strict.xsd because of the referenced dtds. It also has the
			// possibility of blocking on referenced remote URIs.
			// Instead we only resolve local xsds.

			var xmlReader = XmlReader.Create (
				reader,
				new XmlReaderSettings {
					XmlResolver = new LocalOnlyXmlResolver (),
					DtdProcessing = DtdProcessing.Ignore,
					ValidationType = ValidationType.None
				},
				baseUri
			);
			return ReadSchema (xmlReader);
		}

		// ak
		XmlSchema ReadSchema (string fileName, string baseUri)
		{
			using (var reader = new StreamReader (fileName, true))
				return ReadSchema (baseUri, reader);
		}


	}
}