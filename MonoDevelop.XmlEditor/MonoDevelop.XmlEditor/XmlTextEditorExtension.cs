// 
// XmlTextEditorExtension.cs
// 
// Authors:
//   Matt Ward
//   Mikayla Hutchinson <m.j.hutchinson@gmail.com>
// 
// Copyright:
//   (C) 2007 Matt Ward
//   (C) 2008 Novell, Inc (http://www.novell.com)
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
using System.Linq;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Schema;

using MonoDevelop.Core;
using MonoDevelop.Components.Commands;
using MonoDevelop.Projects.Gui;
using MonoDevelop.Projects.Gui.Completion;
using MonoDevelop.Ide.Gui.Content;
using MonoDevelop.XmlEditor.Completion;
using MonoDevelop.Xml.StateEngine;
using MonoDevelop.Ide.Tasks;

namespace MonoDevelop.XmlEditor
{
	
	
	public class XmlTextEditorExtension : MonoDevelop.XmlEditor.Gui.BaseXmlEditorExtension
	{
		const string TextXmlMimeType = "text/xml";
		const string ApplicationXmlMimeType = "application/xml";
		string stylesheetFileName;
		XmlSchemaCompletionData defaultSchemaCompletionData;
		string defaultNamespacePrefix;
		InferredXmlCompletionProvider inferredCompletionData;
		bool inferenceQueued = false;
		
//		bool showSchemaAnnotation;
		
		public XmlTextEditorExtension() : base ()
		{
		}
		
		public override bool ExtendsEditor (MonoDevelop.Ide.Gui.Document doc, IEditableTextBuffer editor)
		{
			if (doc == null)
				return false;
			return IsFileNameHandled (doc.Name);
		}
		
		protected override IEnumerable<string> SupportedExtensions {
			get { throw new System.InvalidOperationException (); }
		}
 
		
		public override void Initialize ()
		{
			base.Initialize ();
			XmlEditorAddInOptions.PropertyChanged += XmlEditorPropertyChanged;
			XmlSchemaManager.UserSchemaAdded += UserSchemaAdded;
			XmlSchemaManager.UserSchemaRemoved += UserSchemaRemoved;
			SetInitialValues();
			
			MonoDevelop.SourceEditor.SourceEditorView view = 
				Document.GetContent<MonoDevelop.SourceEditor.SourceEditorView> ();
			if (view != null && view.Document.SyntaxMode == null) {
				Mono.TextEditor.Highlighting.SyntaxMode mode = 
					Mono.TextEditor.Highlighting.SyntaxModeService.GetSyntaxMode (ApplicationXmlMimeType);
				if (mode != null)
					view.Document.SyntaxMode = mode;
				else
					LoggingService.LogWarning ("XmlTextEditorExtension could not get SyntaxMode for mimetype '" 
					    + ApplicationXmlMimeType + "'.");
			}
		}
		
		bool disposed;
		public override void Dispose()
		{
			if (!disposed) {
				disposed = false;
				XmlEditorAddInOptions.PropertyChanged -= XmlEditorPropertyChanged;
				XmlSchemaManager.UserSchemaAdded -= UserSchemaAdded;
				XmlSchemaManager.UserSchemaRemoved -= UserSchemaRemoved;
				base.Dispose ();
			}
		}
		
		#region Code completion
		
//		IEditableTextBuffer GetBuffer ()
//		{
//			IEditableTextBuffer buf = Document.GetContent<IEditableTextBuffer> ();
//			System.Diagnostics.Debug.Assert (buf != null);
//			return buf;
//		}
		
		XmlElementPath GetCurrentPath ()
		{
			return ConvertPath (Tracker.Engine.Nodes.OfType<XElement> ().Reverse ());
		}
		
		static XmlElementPath ConvertPath (IEnumerable<XElement> path)
		{
			XmlElementPath elementPath = new XmlElementPath ();
			var namespaces = new Dictionary<string, string> ();
			string defaultNamespace = null;
			foreach (XElement el in path) {
				foreach (XAttribute att in el.Attributes) {
					if (att.Name.HasPrefix) {
						if (att.Name.Prefix == "xmlns")
							namespaces [att.Name.Name] = att.Value;
					} else {
						if (att.Name.Name == "xmlns")
							defaultNamespace = att.Value;
					}
				}
				string ns = null;
				if (el.Name.HasPrefix)
					namespaces.TryGetValue (el.Name.Prefix, out ns);
				else
					ns = defaultNamespace;
				QualifiedName qn = new QualifiedName (el.Name.Name, ns, el.Name.Prefix ?? String.Empty);
				elementPath.Elements.Add (qn);
			}
			return elementPath;
		}
		
		protected override void GetElementCompletions (CompletionDataList list)
		{	
			XmlElementPath path = GetCurrentPath ();
			if (path.Elements.Count > 0) {
				IXmlCompletionProvider schema = FindSchema (path);
				if (schema == null)
					schema = inferredCompletionData;
				if (schema != null) {
					ICompletionData[] completionData = schema.GetChildElementCompletionData (path);
					if (completionData != null)
						list.AddRange (completionData);
				}
			} else if (defaultSchemaCompletionData != null) {
				list.AddRange (defaultSchemaCompletionData.GetElementCompletionData (defaultNamespacePrefix));
			} else if (inferredCompletionData != null) {
				list.AddRange (inferredCompletionData.GetElementCompletionData ());
			}
		}
		
		protected override CompletionDataList GetAttributeCompletions (IAttributedXObject attributedOb,
			Dictionary<string, string> existingAtts)
		{
			XmlElementPath path = GetCurrentPath ();
			if (path.Elements.Count > 0) {
				IXmlCompletionProvider schema = FindSchema (path);
				if (schema == null)
					schema = inferredCompletionData;
				if (schema != null) {
					ICompletionData[] completionData = schema.GetAttributeCompletionData (path);
					if (completionData != null)
						return new CompletionDataList (completionData);
				}
			}
			return null;
		}
		
		protected override CompletionDataList GetAttributeValueCompletions (IAttributedXObject attributedOb, XAttribute att)
		{
			XmlElementPath path = GetCurrentPath ();
			if (path.Elements.Count > 0) {
				XmlSchemaCompletionData schema = FindSchema (path);
				if (schema != null) {
					ICompletionData[] completionData = schema.GetAttributeValueCompletionData (path, att.Name.FullName);
					if (completionData != null)
						return new CompletionDataList (completionData);
				}
			}
			return null;
		}
		
		#endregion
		
		#region From XmlCompletionDataProvider.cs
		
		public XmlSchemaCompletionData FindSchemaFromFileName (string fileName)
		{
			return XmlSchemaManager.SchemaCompletionDataItems.GetSchemaFromFileName (fileName);
		}
		
		public XmlSchemaCompletionData FindSchema (string namespaceUri)
		{
			return XmlSchemaManager.SchemaCompletionDataItems[namespaceUri];
		}
		
		public XmlSchemaCompletionData FindSchema (XmlElementPath path)
		{
			return FindSchema (XmlSchemaManager.SchemaCompletionDataItems, path);
		}
		
		/// <summary>
		/// Finds the schema given the xml element path.
		/// </summary>
		public XmlSchemaCompletionData FindSchema (IXmlSchemaCompletionDataCollection schemaCompletionDataItems, XmlElementPath path)
		{
			if (path.Elements.Count > 0) {
				string namespaceUri = path.Elements[0].Namespace;
				if (namespaceUri.Length > 0) {
					return schemaCompletionDataItems[namespaceUri];
				} else if (defaultSchemaCompletionData != null) {
					
					// Use the default schema namespace if none
					// specified in a xml element path, otherwise
					// we will not find any attribute or element matches
					// later.
					foreach (QualifiedName name in path.Elements) {
						if (name.Namespace.Length == 0) {
							name.Namespace = defaultSchemaCompletionData.NamespaceUri;
						}
					}
					return defaultSchemaCompletionData;
				}
			}
			return null;
		}
		
		#endregion
		
		#region Schema resolution
		
		/// <summary>
		/// Gets the XmlSchemaObject that defines the currently selected xml element or attribute.
		/// </summary>
		/// <param name="currentSchemaCompletionData">This is the schema completion data for the schema currently being 
		/// displayed. This can be null if the document is not a schema.</param>
		public XmlSchemaObject GetSchemaObjectSelected (XmlSchemaCompletionData currentSchemaCompletionData)
		{
			// Find element under cursor.
			XmlElementPath path = GetCurrentPath ();
			
			//attribute name under cursor, if valid
			string attributeName = null;
			XAttribute xatt = Tracker.Engine.Nodes.Peek (0) as XAttribute;
			if (xatt != null) {
				XName xattName = xatt.Name;
				if (Tracker.Engine.CurrentState is XmlNameState) {
					xattName = GetCompleteName ();
				}
				attributeName = xattName.FullName;
			}
			
			// Find schema definition object.
			XmlSchemaCompletionData schemaCompletionData = FindSchema (path);
			XmlSchemaObject schemaObject = null;
			if (schemaCompletionData != null) {
				XmlSchemaElement element = schemaCompletionData.FindElement(path);
				schemaObject = element;
				if (element != null) {
					if (attributeName.Length > 0) {
						XmlSchemaAttribute attribute = schemaCompletionData.FindAttribute(element, attributeName);
						if (attribute != null) {
							if (currentSchemaCompletionData != null) {
								schemaObject = GetSchemaObjectReferenced (currentSchemaCompletionData, element, attribute);
							} else {
								schemaObject = attribute;
							}
						}
					}
					return schemaObject;
				}
			}	
			return null;
		}
		
		/// <summary>
		/// If the attribute value found references another item in the schema
		/// return this instead of the attribute schema object. For example, if the
		/// user can select the attribute value and the code will work out the schema object pointed to by the ref
		/// or type attribute:
		///
		/// xs:element ref="ref-name"
		/// xs:attribute type="type-name"
		/// </summary>
		/// <returns>
		/// The <paramref name="attribute"/> if no schema object was referenced.
		/// </returns>
		XmlSchemaObject GetSchemaObjectReferenced (XmlSchemaCompletionData currentSchemaCompletionData, XmlSchemaElement element, XmlSchemaAttribute attribute)
		{
			XmlSchemaObject schemaObject = null;
			if (IsXmlSchemaNamespace(element)) {
				// Find attribute value.
				//fixme implement
				string attributeValue = "";// XmlParser.GetAttributeValueAtIndex(xml, index);
				if (attributeValue.Length == 0) {
					return attribute;
				}
		
				if (attribute.Name == "ref") {
					schemaObject = FindSchemaObjectReference(attributeValue, currentSchemaCompletionData, element.Name);
				} else if (attribute.Name == "type") {
					schemaObject = FindSchemaObjectType(attributeValue, currentSchemaCompletionData, element.Name);
				}
			}
			
			if (schemaObject != null) {
				return schemaObject;
			}
			return attribute;
		}
		
		/// <summary>
		/// Checks whether the element belongs to the XSD namespace.
		/// </summary>
		static bool IsXmlSchemaNamespace (XmlSchemaElement element)
		{
			XmlQualifiedName qualifiedName = element.QualifiedName;
			if (qualifiedName != null) {
				return XmlSchemaManager.IsXmlSchemaNamespace (qualifiedName.Namespace);
			}
			return false;
		}
		
		/// <summary>
		/// Attempts to locate the reference name in the specified schema.
		/// </summary>
		/// <param name="name">The reference to look up.</param>
		/// <param name="schemaCompletionData">The schema completion data to use to
		/// find the reference.</param>
		/// <param name="elementName">The element to determine what sort of reference it is
		/// (e.g. group, attribute, element).</param>
		/// <returns><see langword="null"/> if no match can be found.</returns>
		XmlSchemaObject FindSchemaObjectReference(string name, XmlSchemaCompletionData schemaCompletionData, string elementName)
		{
			QualifiedName qualifiedName = schemaCompletionData.CreateQualifiedName(name);
			XmlSchemaCompletionData qualifiedNameSchema = FindSchema(qualifiedName.Namespace);
			if (qualifiedNameSchema != null) {
				schemaCompletionData = qualifiedNameSchema;
			}
			switch (elementName) {
				case "element":
					return schemaCompletionData.FindElement(qualifiedName);
				case "attribute":
					return schemaCompletionData.FindAttribute(qualifiedName.Name);
				case "group":
					return schemaCompletionData.FindGroup(qualifiedName.Name);
				case "attributeGroup":
					return schemaCompletionData.FindAttributeGroup(qualifiedName.Name);
			}
			return null;
		}
		
		/// <summary>
		/// Attempts to locate the type name in the specified schema.
		/// </summary>
		/// <param name="name">The type to look up.</param>
		/// <param name="schemaCompletionData">The schema completion data to use to
		/// find the type.</param>
		/// <param name="elementName">The element to determine what sort of type it is
		/// (e.g. group, attribute, element).</param>
		/// <returns><see langword="null"/> if no match can be found.</returns>
		XmlSchemaObject FindSchemaObjectType(string name, XmlSchemaCompletionData schemaCompletionData, string elementName)
		{
			QualifiedName qualifiedName = schemaCompletionData.CreateQualifiedName(name);
			XmlSchemaCompletionData qualifiedNameSchema = FindSchema(qualifiedName.Namespace);
			if (qualifiedNameSchema != null) {
				schemaCompletionData = qualifiedNameSchema;
			}
			switch (elementName) {
				case "element":
					return schemaCompletionData.FindComplexType(qualifiedName);
				case "attribute":
					return schemaCompletionData.FindSimpleType(qualifiedName.Name);
			}
			return null;
		}
		
		#endregion
		
		#region Settings handling
		
		void SetDefaultSchema (string fileName)
		{
			if (fileName == null) {
				return;
			}
			string extension = System.IO.Path.GetExtension (fileName).ToLower ();
			defaultSchemaCompletionData = XmlSchemaManager.GetSchemaCompletionData (extension);
			if (defaultSchemaCompletionData != null)
				inferredCompletionData = null;
			else
				QueueInference ();
			defaultNamespacePrefix = XmlSchemaManager.GetNamespacePrefix (extension);
		}
		
		/// Updates the default schema association since the schema may have been added.
		void UserSchemaAdded (object source, EventArgs e)
		{	
			SetDefaultSchema (Document.Name);
		}
		
		// Updates the default schema association since the schema may have been removed.
		void UserSchemaRemoved (object source, EventArgs e)
		{
			SetDefaultSchema (Document.Name);
		}
		
		void XmlEditorPropertyChanged (object sender, PropertyChangedEventArgs args)
 		{
			switch (args.Key) {
			case "AutoCompleteElements":
				AutoCompleteClosingTags = XmlEditorAddInOptions.AutoCompleteElements;
				break;
			case "ShowSchemaAnnotation":
//				showSchemaAnnotation = XmlEditorAddInOptions.ShowSchemaAnnotation;
				break;
			default:
				string extension = System.IO.Path.GetExtension (Document.Name).ToLower ();
				if (args.Key == extension) {
					SetDefaultSchema (Document.Name);
				} else {
					LoggingService.LogError ("Unhandled property change in XmlTextEditorExtension: " + args.Key);
				}
				break;
			}
		}
		
		void SetInitialValues()
		{
//			showSchemaAnnotation = XmlEditorAddInOptions.ShowSchemaAnnotation;
			AutoCompleteClosingTags = XmlEditorAddInOptions.AutoCompleteElements;
			SetDefaultSchema (Document.Name);
		}
		
		#endregion
		
		#region Stylesheet handling
		
		/// <summary>
		/// Gets or sets the stylesheet associated with this xml file.
		/// </summary>
		public string StylesheetFileName {
			get { return stylesheetFileName; }
			set { stylesheetFileName = value; }
		}
						
		#endregion
		
		#region Filetype/schema detection		
		
		public bool IsSchema {
			get {
				string extension = System.IO.Path.GetExtension (FileName);
				if (extension != null)
					return String.Compare (extension, ".xsd", true) == 0;
				return false;
			}
		}
		
		/// <summary>
		/// Determines whether the file can be displayed by
		/// the xml editor.
		/// </summary>
		public static bool IsFileNameHandled (string fileName)
		{			
			if (fileName == null)
				return false;
			
			if (System.IO.Path.IsPathRooted (fileName)) {
				string vfsname = fileName.Replace ("%", "%25").Replace ("#", "%23").Replace ("?", "%3F");
				string mimeType = MonoDevelop.Core.Gui.DesktopService.GetMimeTypeForUri (vfsname);
				if (IsMimeTypeHandled (mimeType))
					return true;
			}
			
			return XmlFileExtensions.IsXmlFileExtension (System.IO.Path.GetExtension (fileName));
		}
		
		public static bool IsMimeTypeHandled (string mimeType)
		{
			return (mimeType != null && (mimeType == TextXmlMimeType || mimeType == ApplicationXmlMimeType));
		}
			
		#endregion
		
		#region Smart indent
		
		public override bool KeyPress (Gdk.Key key, char keyChar, Gdk.ModifierType modifier)
		{
			bool result;
			
			if (TextEditorProperties.IndentStyle == IndentStyle.Smart && key == Gdk.Key.Return) {
				result = base.KeyPress (key, keyChar, modifier);
				SmartIndentLine (Editor.CursorLine);
				return result;
			}
			return base.KeyPress (key, keyChar, modifier);
		}
		
		void SmartIndentLine (int line)
		{
			//FIXME: implement this
		}
		
//		string GetLineIndent (int line)
//		{
//			string indent = string.Empty;
//			int start = Editor.GetPositionFromLineColumn (line, 1);
//			int i = start;
//			while (i < Editor.TextLength) {
//				char c = Editor.GetCharAt (i);
//				if (c == '\n' || c == '\r')
//					break;
//				if (!char.IsWhiteSpace (c))
//					break;
//				i++;
//			}
//			if (i > 0)
//				indent = Editor.GetText (start, i);
//			return indent;
//		}
		
		//gets the indent of the line containing this position, up to the position index
		string GetPositionIndent (int position)
		{
			int indentEnd = position;
			int i = position - 1;
			while (i > 0) {
				char c = Editor.GetCharAt (i);
				if (c == '\n' || c == '\r')
					return Editor.GetText (i + 1, indentEnd);
				if (!char.IsWhiteSpace (c))
					indentEnd--;
				i--;
			}
			return null;
		}
		
		#endregion
		
		#region Command handlers
		
		[CommandUpdateHandler (MonoDevelop.Ide.Commands.EditCommands.ToggleCodeComment)]
		protected void ToggleCodeCommentCommandUpdate (CommandInfo info)
		{
			info.Enabled = false;
		}
		
		[CommandHandler (MonoDevelop.Ide.Commands.EditCommands.ToggleCodeComment)]
		public void ToggleCodeCommentCommand ()
		{
			//FIXME: implement
		}
		
		[CommandHandler (Commands.Format)]
		public void FormatCommand ()
		{
			TaskService.Errors.Clear ();
			
			using (IProgressMonitor monitor = XmlEditorService.GetMonitor ()) {
				bool selection = (Editor.SelectionEndPosition - Editor.SelectionStartPosition) > 0;
				string xml = selection? Editor.SelectedText : Editor.Text;
				XmlDocument doc = XmlEditorService.ValidateWellFormedness (monitor, xml, FileName);
				if (doc == null)
					return;
				
				//if there's a line indent at the current location, prepend that to all new lines
				string extraIndent = null;
				if (selection)
					extraIndent = GetPositionIndent (Editor.SelectionStartPosition);
				
				string formattedXml = XmlEditorService.IndentedFormat (xml);
				
				//convert newlines and prepend extra indents to each line if needed
				bool nonNativeNewline = (Editor.NewLine != Environment.NewLine);
				bool hasExtraIndent = !string.IsNullOrEmpty (extraIndent);
				if (hasExtraIndent || nonNativeNewline) {
					System.Text.StringBuilder builder = new System.Text.StringBuilder (formattedXml);
					
					if (nonNativeNewline)
						builder.Replace (Environment.NewLine, Editor.NewLine);
					
					if (hasExtraIndent) {
						builder.Replace (Editor.NewLine, Editor.NewLine + extraIndent);
						if (formattedXml.EndsWith (Environment.NewLine))
							builder.Remove (builder.Length - 1 - extraIndent.Length, extraIndent.Length);
					}
					formattedXml = builder.ToString ();
				}
				
				Editor.BeginAtomicUndo ();
				if (selection) {
					Editor.SelectedText = formattedXml;
				} else {
					Editor.DeleteText (0, Editor.TextLength);
					Editor.InsertText (0, formattedXml);
				}
				Editor.EndAtomicUndo ();
			}
		}
		
		[CommandHandler (Commands.CreateSchema)]
		public void CreateSchemaCommand ()
		{
			try {
				TaskService.Errors.Clear ();
				string xml = Editor.Text;
				using (IProgressMonitor monitor = XmlEditorService.GetMonitor ()) {
					XmlDocument doc = XmlEditorService.ValidateWellFormedness (monitor, xml, FileName);
					if (doc == null)
						return;
					monitor.BeginTask (GettextCatalog.GetString ("Creating schema..."), 0);
					try {
						string schema = XmlEditorService.CreateSchema (xml);
						string fileName = XmlEditorService.GenerateFileName (FileName, "{0}.xsd");
						MonoDevelop.Ide.Gui.IdeApp.Workbench.NewDocument (fileName, "application/xml", schema);
						monitor.ReportSuccess (GettextCatalog.GetString ("Schema created."));
					} catch (Exception ex) {
						string msg = GettextCatalog.GetString ("Error creating XML schema.");
						LoggingService.LogError (msg, ex);
						monitor.ReportError (msg, ex);
					}
				}
			} catch (Exception ex) {
				MonoDevelop.Core.Gui.MessageService.ShowError(ex.Message);
			}
		}
		
		[CommandHandler (Commands.OpenStylesheet)]
		public void OpenStylesheetCommand ()
		{
			if (!string.IsNullOrEmpty (stylesheetFileName)) {
				try {
					MonoDevelop.Ide.Gui.IdeApp.Workbench.OpenDocument (stylesheetFileName);
				} catch (Exception ex) {
					MonoDevelop.Core.LoggingService.LogError ("Could not open document.", ex);
					MonoDevelop.Core.Gui.MessageService.ShowException (ex, "Could not open document.");
				}
			}
		}
		
		[CommandUpdateHandler (Commands.OpenStylesheet)]
		public void UpdateOpenStylesheetCommand (CommandInfo info)
		{
			info.Enabled = !string.IsNullOrEmpty (stylesheetFileName);
		}
		
		[CommandHandler (Commands.GoToSchemaDefinition)]
		public void GoToSchemaDefinitionCommand ()
		{
			try {
				//try to resolve the schema
				XmlSchemaCompletionData currentSchemaCompletionData = FindSchemaFromFileName (FileName);						
				XmlSchemaObject schemaObject = GetSchemaObjectSelected (currentSchemaCompletionData);
				
				// Open schema if resolved
				if (schemaObject != null && schemaObject.SourceUri != null && schemaObject.SourceUri.Length > 0) {
					string schemaFileName = schemaObject.SourceUri.Replace ("file:/", String.Empty);
					MonoDevelop.Ide.Gui.IdeApp.Workbench.OpenDocument (
					    schemaFileName,
					    Math.Max (1, schemaObject.LineNumber),
					    Math.Max (1, schemaObject.LinePosition), true);
				}
			} catch (Exception ex) {
				MonoDevelop.Core.LoggingService.LogError ("Could not open document.", ex);
				MonoDevelop.Core.Gui.MessageService.ShowException (ex, "Could not open document.");
			}
		}
		
		[CommandHandler (Commands.Validate)]
		public void ValidateCommand ()
		{
			TaskService.Errors.Clear ();
			using (IProgressMonitor monitor = XmlEditorService.GetMonitor()) {
				if (IsSchema)
					XmlEditorService.ValidateSchema (monitor, Editor.Text, FileName);
				else
					XmlEditorService.ValidateXml (monitor, Editor.Text, FileName);
			}
		}
		
		[CommandHandler (Commands.AssignStylesheet)]
		public void AssignStylesheetCommand ()
		{
			// Prompt user for filename.
			string fileName = XmlEditorService.BrowseForStylesheetFile ();
			if (!string.IsNullOrEmpty (stylesheetFileName))
				stylesheetFileName = fileName;
		}
		
		[CommandHandler (Commands.RunXslTransform)]
		public void RunXslTransformCommand ()
		{
			if (string.IsNullOrEmpty (stylesheetFileName)) {
				stylesheetFileName = XmlEditorService.BrowseForStylesheetFile ();
				if (string.IsNullOrEmpty (stylesheetFileName))
					return;
			}
			
			using (IProgressMonitor monitor = XmlEditorService.GetMonitor()) {
				try {
					string xsltContent;
					try {
						xsltContent = GetFileContent (stylesheetFileName);	
					} catch (System.IO.IOException) {
						monitor.ReportError (
						    GettextCatalog.GetString ("Error reading file '{0}'.", stylesheetFileName), null);
						return;
					}
					System.Xml.Xsl.XslTransform xslt = 
						XmlEditorService.ValidateStylesheet (monitor, xsltContent, stylesheetFileName);
					if (xslt == null)
						return;
					
					XmlDocument doc = XmlEditorService.ValidateXml (monitor, Editor.Text, FileName);
					if (doc == null)
						return;
					
					string newFileName = XmlEditorService.GenerateFileName (FileName, "-transformed{0}.xml");
					
					monitor.BeginTask (GettextCatalog.GetString ("Executing transform..."), 1);
					using (XmlTextWriter output = XmlEditorService.CreateXmlTextWriter()) {
						xslt.Transform (doc, null, output);
						MonoDevelop.Ide.Gui.IdeApp.Workbench.NewDocument (
						    newFileName, "application/xml", output.ToString ());
					}
					monitor.ReportSuccess (GettextCatalog.GetString ("Transform completed."));
					monitor.EndTask ();
				} catch (Exception ex) {
					string msg = GettextCatalog.GetString ("Could not run transform.");
					monitor.ReportError (msg, ex);
					monitor.EndTask ();
				}
			}
		}
		
		string GetFileContent (string fileName)
		{
			MonoDevelop.Projects.Text.IEditableTextFile tf =
				MonoDevelop.DesignerSupport.OpenDocumentFileProvider.Instance.GetEditableTextFile (fileName);
			if (tf != null)
				return tf.Text;
			System.IO.StreamReader reader = new System.IO.StreamReader (fileName, true);
			return reader.ReadToEnd();
		}
		
		#endregion
		
		void QueueInference ()
		{
			XmlParsedDocument doc = this.CU as XmlParsedDocument;
			if (defaultSchemaCompletionData != null || doc == null || doc.XDocument == null || inferenceQueued)
				return;
			if (inferredCompletionData == null
			    || (doc.ParseTime - inferredCompletionData.TimeStamp).TotalSeconds >= 5
			        && doc.Errors.Count <= inferredCompletionData.ErrorCount)
			{
				inferenceQueued = true;
				System.Threading.ThreadPool.QueueUserWorkItem (delegate {
					InferredXmlCompletionProvider newData = new InferredXmlCompletionProvider ();
					newData.Populate (doc.XDocument);
					newData.TimeStamp = DateTime.Now;
					newData.ErrorCount = doc.Errors.Count;
					this.inferenceQueued = false;
					this.inferredCompletionData = newData;
				});
			}	
		}
		
		protected override void OnParsedDocumentUpdated ()
		{
			QueueInference ();
			base.OnParsedDocumentUpdated ();
		}		
	}
}
