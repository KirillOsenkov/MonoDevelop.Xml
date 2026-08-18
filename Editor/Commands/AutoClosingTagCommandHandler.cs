// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System;
using System.ComponentModel.Composition;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;

using MonoDevelop.Xml.Dom;
using MonoDevelop.Xml.Editor.Logging;
using MonoDevelop.Xml.Editor.Options;
using MonoDevelop.Xml.Editor.Overtype;
using MonoDevelop.Xml.Editor.Parsing;
using MonoDevelop.Xml.Logging;
using MonoDevelop.Xml.Parser;

namespace MonoDevelop.Xml.Editor.Commands
{
	[Name (Name)]
	[Export (typeof (ICommandHandler))]
	[Order (Before = PredefinedCompletionNames.CompletionCommandHandler)]
	[ContentType(XmlContentTypeNames.XmlCore)]
	[TextViewRole (PredefinedTextViewRoles.Interactive)]
	class AutoClosingTagCommandHandler : IChainedCommandHandler<TypeCharCommandArgs>
	{
		const string Name = "Closing Tag Completion";

		[ImportingConstructor]
		public AutoClosingTagCommandHandler (XmlParserProvider parserProvider, IAsyncCompletionBroker completionBroker, IEditorLoggerFactory loggerFactory)
		{
			this.parserProvider = parserProvider;
			this.completionBroker = completionBroker;
			this.loggerFactory = loggerFactory;
		}

		readonly IAsyncCompletionBroker completionBroker;
		readonly IEditorLoggerFactory loggerFactory;
		readonly XmlParserProvider parserProvider;

		public string DisplayName => Name;

		public void ExecuteCommand (TypeCharCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
		{
			// characters the user types over text this handler auto-inserted just before the caret (the > after "/",
			// the "name>" after "</") are consumed by XmlOvertypeCommandHandler via the XmlOvertypeSession started
			// at the end of InsertCloseBracketForSelfClosingTag, so muscle-typing them doesn't duplicate the text (Helix #2986)

			// The completion handler both commits the existing selection and re-triggers,
			// however, it chains to other handlers _before_ it commits, so its undo comes
			// after them. although that's desirable for character insertion and brace completions
			// it's not what we want for end tag completion, as end tag completion is based
			// on the committed value and therefore should be undone before it.
			//
			// Hence this handler comes _before_ the completion handler, but very much like
			// the completion handler itself, it chains before making its own edits.
			var versionBefore = args.SubjectBuffer.CurrentSnapshot.Version.VersionNumber;
			nextCommandHandler ();

			// nothing was typed (e.g. the character was consumed by an XmlOvertypeSession), so nothing to complete
			if (args.SubjectBuffer.CurrentSnapshot.Version.VersionNumber == versionBefore) {
				return;
			}

			try {
				// indenting a hand-typed closing tag like its start tag is independent of auto-insertion
				if (args.TypedChar == '>') {
					AlignTypedClosingTag (args);
				}

				if (!args.TextView.Options.GetAutoInsertClosingTag()) {
					return;
				}

				if (args.TypedChar == '>') {
					InsertCloseTag (args, executionContext);
				}

				if (args.TypedChar == '/') {
					InsertCloseBracketForSelfClosingTag (args, executionContext);
				}
			}
			catch (Exception ex) {
				loggerFactory.GetLogger<AutoClosingTagCommandHandler>(args.TextView).LogInternalException(ex);
			}
		}

		void InsertCloseTag (TypeCharCommandArgs args, CommandExecutionContext executionContext)
		{
			if (!parserProvider.TryGetParser (args.SubjectBuffer, out var parser)) {
				return;
			}

			var multiSelectionBroker = args.TextView.GetMultiSelectionBroker ();
			if (multiSelectionBroker.HasMultipleSelections) {
				return;
			}

			var position = args.TextView.Caret.Position.BufferPosition;
			var spineParser = parser.GetSpineParser (position);
			var el = spineParser.Spine.Peek () as XElement;
			if (el == null || !el.IsEnded || !el.IsNamed || el.Span.End != position.Position) {
				return;
			}

			el = (XElement) spineParser.Spine.Peek ();
			spineParser.AdvanceUntilClosed (el, position.Snapshot);

			// also check for orphaned closing tags in this element's parent
			// this is not as accurate as the tree parse we did above as it uses
			// the last parse result, which might be a little stale
			var lastParseResult = parser.LastOutput;
			if (lastParseResult != null && el.Parent != null) {
				if (lastParseResult.XDocument.FindAtOffset (el.Parent.Span.Start + 1) is XContainer parent) {
					foreach (var n in parent.Nodes) {
						if (n is XClosingTag) {
							return;
						}
					}
				}
			}

			// When the completion handler triggers a new completion session immediately after
			// committing, it created a tracking ApplicableToSpan. When we make our edit, the
			// tracking spand expands to include it, so when the new completion session is
			// committed, it erases our edit.
			//
			// Since we cannot update the ApplicableToSpan at this point, we explicitly dismiss and
			// re-trigger completion instead.

			var completionSession = completionBroker.GetSession (args.TextView);
			if (completionSession != null) {
				completionSession.Dismiss ();
			}

			var bufferEdit = args.SubjectBuffer.CreateEdit ();

			// if there's an extra > after committing due to VS losing track of overtype, delete it
			ITextSnapshot snapshot = args.SubjectBuffer.CurrentSnapshot;
			if (snapshot.Length > position && snapshot[position] == '>') {
				bufferEdit.Delete (new Span (position, 1));
			}

			bufferEdit.Insert (position, $"</{el.Name.FullName}>");

			bufferEdit.Apply ();
			snapshot = args.SubjectBuffer.CurrentSnapshot;

			args.TextView.Caret.MoveTo (new SnapshotPoint (snapshot, position));

			if (completionSession != null) {
				var trigger = new CompletionTrigger (
					CompletionTriggerReason.Insertion, args.SubjectBuffer.CurrentSnapshot, '>');
				var location = args.TextView.Caret.Position.BufferPosition;
				var token = executionContext.OperationContext.UserCancellationToken;

				completionSession = completionBroker.GetSession (args.TextView);
				if (completionSession == null) {
					completionSession = completionBroker.TriggerCompletion (args.TextView, trigger, location, token);
				}

				completionSession?.OpenOrUpdate (trigger, location, token);
			}

			return;
		}

		private enum ClosingTagInsertionMode
		{
			None,
			InsertCloseBracketAfterSlash,
			CompleteClosingTagAfterOpenBracket,
			InsertEntireClosingTag
		}

		void InsertCloseBracketForSelfClosingTag(TypeCharCommandArgs args, CommandExecutionContext executionContext)
		{
			var buffer = args.SubjectBuffer;
			var view = args.TextView;

			if (!parserProvider.TryGetParser(buffer, out var parser)) {
				return;
			}

			var multiSelectionBroker = view.GetMultiSelectionBroker();
			if (multiSelectionBroker.HasMultipleSelections) {
				return;
			}

			var position = view.Caret.Position.BufferPosition;
			if (position < 2) {
				return;
			}

			if ((position - 1).GetChar() != '/') {
				return;
			}

			var mode = ClosingTagInsertionMode.InsertEntireClosingTag;

			var spineParser = parser.GetSpineParser(position);
			var currentState = spineParser.CurrentState;
			var el = spineParser.Spine.OfType<XElement>().FirstOrDefault();

			bool hasCharAtPosition = position < position.Snapshot.Length;
			if (hasCharAtPosition && position.GetChar() == '>') {
				if (currentState is XmlTagState) {
					char previous = (position - 2).GetChar();
					if (previous == '"' || char.IsLetter(previous)) {
						buffer.Insert(position - 1, " ");
					}

					// if we are followed by the separate closing tag, delete it
					if (el != null && el.IsNamed && currentState is XmlTagState) {
						var snapshot = position.Snapshot;
						string closingTagText = $"></{el.Name.FullName}>";
						if (snapshot.Length >= position.Position + closingTagText.Length &&
							snapshot.GetText(position.Position, closingTagText.Length) == closingTagText) {
							buffer.Delete(new Span(position + 1, closingTagText.Length - 1));
						}
					}

					// the > after the caret now closes this self-closing tag; let the user type it over
					var caretBeforeBracket = view.Caret.Position.BufferPosition;
					if (caretBeforeBracket.Position < caretBeforeBracket.Snapshot.Length && caretBeforeBracket.GetChar () == '>') {
						XmlOvertypeSessions.Get (view).Start (new Span (caretBeforeBracket.Position, 0), new Span (caretBeforeBracket.Position, 1));
					}
				}

				return;
			}

			if (el == null || !el.IsNamed || el.IsClosed || el.Name.FullName == null) {
				return;
			}

			string name = el.Name.FullName;
			bool needsSpace = false;

			var previousChar = (position - 2).GetChar();
			if (previousChar == '<') {
				mode = ClosingTagInsertionMode.CompleteClosingTagAfterOpenBracket;
				if (currentState is not XmlClosingTagState) {
					return;
				}

				// the closing tag may already be there: "</|y>" typed over, or "</|/y>" when the / was typed over an existing one
				string closingTagRest = $"{name}>";
				if (IsFollowedBy(position, closingTagRest)) {
					return;
				}

				if (IsFollowedBy(position, "/" + closingTagRest)) {
					// overtype: keep the typed / and drop the existing one so the caret ends up after it
					buffer.Delete(new Span(position, 1));
					return;
				}

				// "<y>text</|</y>": the closing tag is already right after the caret (typically auto-inserted
				// when the start tag was typed). Treat the typed </ as overtyping it: drop the existing tag
				// and complete the typed one, instead of ending up with "</y></y>".
				string closingTag = "</" + closingTagRest;
				if (IsFollowedBy(position, closingTag)) {
					buffer.Delete(new Span(position, closingTag.Length));
				}
			} else if (!el.IsComplete) {
				if (currentState is not XmlTagState) {
					return;
				}

				mode = ClosingTagInsertionMode.InsertCloseBracketAfterSlash;
				if (previousChar == '"' || char.IsLetter(previousChar)) {
					needsSpace = true;
				}
			}
			else {
				if (currentState is not XmlTextState) {
					return;
				}

				// the spine parser only knows the text up to the caret; use the last full parse to see whether
				// this element is already closed further on. still insert when an ancestor is unclosed, as
				// then the closing tag found by the parser most likely belongs to that ancestor.
				if (parser.LastOutput?.XDocument.FindAtOffset(el.Span.Start + 1) is XElement parsedElement
					&& parsedElement.IsClosed
					&& !HasUnclosedAncestor(parsedElement)) {
					return;
				}
			}

			var completionSession = completionBroker.GetSession(view);
			if (completionSession != null) {
				completionSession.Dismiss();
			}

			int caretOffset = 0;

			using (var bufferEdit = buffer.CreateEdit()) {
				if (mode == ClosingTagInsertionMode.CompleteClosingTagAfterOpenBracket) {
					bufferEdit.Insert(position, $"{name}>");
					caretOffset += name.Length + 1;
					caretOffset += AlignClosingTagWithStartTag(bufferEdit, el, position - 2);
				}
				else if (mode == ClosingTagInsertionMode.InsertEntireClosingTag) {
					bufferEdit.Delete(position - 1, 1);
					bufferEdit.Insert(position, $"</{name}>");
					caretOffset += name.Length + 2;
				}
				else if (mode == ClosingTagInsertionMode.InsertCloseBracketAfterSlash) {
					if (needsSpace) {
						bufferEdit.Insert(position - 1, " ");
						caretOffset++;
					}

					bufferEdit.Insert(position, $">");
					caretOffset++;
				}

				bufferEdit.Apply();
			}

			var newPosition = new SnapshotPoint(buffer.CurrentSnapshot, position.Position + caretOffset);
			var topBufferPosition = view.BufferGraph.MapUpToBuffer(newPosition, PointTrackingMode.Positive, PositionAffinity.Successor, view.TextBuffer);
			if (topBufferPosition.HasValue)
			{
				view.Caret.MoveTo(topBufferPosition.Value);
			}

			// what the user would type next if they kept going by hand: ">" after "/", "name>" after "</" or "/"
			string autoInserted = mode == ClosingTagInsertionMode.InsertCloseBracketAfterSlash ? ">" : name + ">";
			var caretAfterInsert = view.Caret.Position.BufferPosition;
			if (caretAfterInsert.Position >= autoInserted.Length
				&& caretAfterInsert.Snapshot.GetText (caretAfterInsert.Position - autoInserted.Length, autoInserted.Length) == autoInserted) {
				XmlOvertypeSessions.Get (view).StartPrefix (new Span (caretAfterInsert.Position - autoInserted.Length, autoInserted.Length));
			}
		}

		// After typing the > of a closing tag ("</name>"), indent that tag like its start tag (Helix #3188)
		void AlignTypedClosingTag (TypeCharCommandArgs args)
		{
			var view = args.TextView;
			var buffer = args.SubjectBuffer;

			if (view.GetMultiSelectionBroker ().HasMultipleSelections) {
				return;
			}

			if (!parserProvider.TryGetParser (buffer, out var parser)) {
				return;
			}

			var position = view.Caret.Position.BufferPosition;
			var snapshot = position.Snapshot;
			int i = position.Position - 1;
			if (i < 0 || snapshot[i] != '>') {
				return;
			}

			int nameEnd = i;
			i--;
			while (i >= 0 && XmlChar.IsNameChar (snapshot[i])) {
				i--;
			}

			int nameStart = i + 1;
			if (nameStart == nameEnd || i < 1 || snapshot[i] != '/' || snapshot[i - 1] != '<') {
				return;
			}

			int closingTagStart = i - 1;
			var spineParser = parser.GetSpineParser (new SnapshotPoint (snapshot, nameStart));
			var el = spineParser.Spine.OfType<XElement> ().FirstOrDefault ();
			if (el == null || !el.IsNamed || el.Name.FullName != snapshot.GetText (nameStart, nameEnd - nameStart)) {
				return;
			}

			using var bufferEdit = buffer.CreateEdit ();
			if (AlignClosingTagWithStartTag (bufferEdit, el, closingTagStart) != 0) {
				bufferEdit.Apply ();
			} else {
				bufferEdit.Cancel ();
			}
		}

		static bool IsFollowedBy (SnapshotPoint position, string text)
		{
			var snapshot = position.Snapshot;
			return snapshot.Length >= position.Position + text.Length
				&& snapshot.GetText (position.Position, text.Length) == text;
		}

		static bool HasUnclosedAncestor (XElement element)
		{
			for (var parent = element.Parent; parent != null; parent = parent.Parent) {
				if (parent is XElement parentElement && !parentElement.IsClosed) {
					return true;
				}
			}

			return false;
		}

		// When the closing tag is alone on its line, indent it like the line of the start tag.
		// Returns the change in length before the closing tag so the caller can adjust the caret.
		static int AlignClosingTagWithStartTag (ITextEdit bufferEdit, XElement el, int closingTagStart)
		{
			var snapshot = bufferEdit.Snapshot;
			var closingTagLine = snapshot.GetLineFromPosition (closingTagStart);
			var startTagLine = snapshot.GetLineFromPosition (el.Span.Start);
			if (closingTagLine.LineNumber == startTagLine.LineNumber) {
				return 0;
			}

			string closingIndent = snapshot.GetText (closingTagLine.Start, closingTagStart - closingTagLine.Start);
			if (closingIndent.Trim ().Length > 0) {
				return 0;
			}

			string startTagLineText = startTagLine.GetText ();
			int startIndentLength = 0;
			while (startIndentLength < startTagLineText.Length && char.IsWhiteSpace (startTagLineText[startIndentLength])) {
				startIndentLength++;
			}

			string startIndent = startTagLineText.Substring (0, startIndentLength);
			if (startIndent == closingIndent) {
				return 0;
			}

			bufferEdit.Replace (new Span (closingTagLine.Start, closingIndent.Length), startIndent);
			return startIndent.Length - closingIndent.Length;
		}

		public CommandState GetCommandState (TypeCharCommandArgs args, Func<CommandState> nextCommandHandler)
			=> nextCommandHandler ();
	}
}
