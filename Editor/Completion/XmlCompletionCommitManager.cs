// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

using Microsoft.CodeAnalysis.Text.Shared.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor.Commanding;
using Microsoft.VisualStudio.Text.Editor.OptionsExtensionMethods;
using Microsoft.VisualStudio.Text.Projection;
using Microsoft.VisualStudio.Threading;

using MonoDevelop.Xml.Dom;
using MonoDevelop.Xml.Editor.Options;
using MonoDevelop.Xml.Editor.Overtype;
using MonoDevelop.Xml.Parser;

namespace MonoDevelop.Xml.Editor.Completion;

class XmlCompletionCommitManager (ILogger logger, JoinableTaskContext joinableTaskContext, IEditorCommandHandlerServiceFactory commandServiceFactory)
	: AbstractCompletionCommitManager<XmlCompletionTrigger, XmlCompletionItemKind>(logger, joinableTaskContext, commandServiceFactory)
{
	public override IEnumerable<char> PotentialCommitCharacters => allCommitChars;

	static readonly char[] allCommitChars = { '>', '/', '\\', '=', ' ', ';', '"', '\'' };
	static readonly char[] attributeCommitChars = { '=', ' ', '"', '\'', '/' };
	static readonly char[] tagCommitChars = { '>', '/', ' ' };
	static readonly char[] entityCommitChars = { ';' };
	static readonly char[] attributeValueCommitChars = { '"', '\'' };
	static readonly char[] pathSeparators = { '/', '\\' };

	/// <summary>
	/// A completion source completing paths sets this session property to <c>true</c>: typing a path separator then commits
	/// the selected item (a folder's insert text already ends with the separator, so the typed one is swallowed).
	/// </summary>
	public static readonly object CommitOnPathSeparatorKey = new ();

	static bool IsPathSeparator (char c) => Array.IndexOf (pathSeparators, c) > -1;

	static bool CommitsOnPathSeparator (IAsyncCompletionSession session)
		=> session.Properties.TryGetProperty (CommitOnPathSeparatorKey, out bool commits) && commits;

	static char[] GetCommitChars (XmlCompletionTrigger trigger)
		=> trigger switch {
			XmlCompletionTrigger.Tag or
			XmlCompletionTrigger.ElementName or
			XmlCompletionTrigger.ElementValue or
			XmlCompletionTrigger.DocType => tagCommitChars,
			XmlCompletionTrigger.AttributeName => attributeCommitChars,
			XmlCompletionTrigger.AttributeValue => attributeValueCommitChars,
			XmlCompletionTrigger.Entity => entityCommitChars,
			XmlCompletionTrigger.DeclarationOrCDataOrComment => allCommitChars,
			_ => throw new ArgumentOutOfRangeException ($"Unhandled value XmlCompletionTrigger.{trigger}")
		};

	protected override bool IsCommitCharForTriggerKind (XmlCompletionTrigger trigger, IAsyncCompletionSession session, ITextSnapshot snapshot, char typedChar)
	{
		switch (trigger) {
		case XmlCompletionTrigger.Tag:
		case XmlCompletionTrigger.ElementName:
		case XmlCompletionTrigger.ElementValue:
			// allow using / as a commit char for elements as self-closing elements, but special case disallowing it
			// in the cases where that could conflict with typing the / at the start of a closing tag
			if (typedChar == '/') {
				var span = session.ApplicableToSpan.GetSpan (session.ApplicableToSpan.TextBuffer.CurrentSnapshot);
				if (trigger switch {
					XmlCompletionTrigger.ElementName => span.Length == 0,
					// XmlCompletionTrigger.ElementValue may have values that are not tags so check the < as well
					_ => span.Length == 1 && span.Start.GetChar() == '<'
				}) {
					return false;
				}
			}
			goto default;
		case XmlCompletionTrigger.AttributeValue:
			if (IsPathSeparator (typedChar) && CommitsOnPathSeparator (session)) {
				return true;
			}
			goto default;
		default:
			var commitChars = GetCommitChars (trigger);
			return Array.IndexOf (commitChars, typedChar) > -1;
		}
	}

	protected override CommitResult TryCommitItemKind (XmlCompletionItemKind itemKind, IAsyncCompletionSession session, ITextBuffer buffer, CompletionItem item, char typedChar, CancellationToken token)
	{
		var topBufferApplicableToSpan = session.ApplicableToSpan;

		var span = topBufferApplicableToSpan.GetSpan (topBufferApplicableToSpan.TextBuffer.CurrentSnapshot);
		if (span.Snapshot.TextBuffer is IProjectionBuffer projectionBuffer)
		{
			var mappedDown = session.TextView.BufferGraph.MapDownToBuffer(span, SpanTrackingMode.EdgeNegative, buffer);
			if (mappedDown.Count < 1)
			{
				return CommitResult.Unhandled;
			}

			span = mappedDown[0];
		}

		bool wasTypedInFull = span.Length == item.InsertText.Length;

		switch (itemKind) {
		case XmlCompletionItemKind.SelfClosingElement: {
				//committing self-closing element with > makes it non-self-closing
				if (typedChar == '>') {
					goto case XmlCompletionItemKind.Element;
				}

				ExtendSpanToConsume (ref span, '>');

				string insertionText = $"{item.InsertText}/>";
				ReplaceSpanAndMoveCaret (session, buffer, span, insertionText, item.InsertText.Length);

				// don't insert double /
				if (typedChar == '/' && !wasTypedInFull) {
					return CommitSwallowChar;
				}

				return CommitResult.Handled;
			}
		case XmlCompletionItemKind.Attribute: {
				// when the caret is mid-name ("Vert|ical"), the span only reaches the caret;
				// consume the rest of the name so the commit replaces the whole name
				var nameSnapshot = span.Snapshot;
				while (span.End < nameSnapshot.Length && XmlChar.IsNameChar (nameSnapshot[span.End])) {
					span = new SnapshotSpan (nameSnapshot, span.Start, span.Length + 1);
				}

				// simple handling if it might interfere with typing or auto attributes are disabled
				if (typedChar == '=' || typedChar == ' ' || !session.TextView.Options.GetAutoInsertAttributeValue ()) {
					ReplaceSpan (buffer, span, item.InsertText);
					return CommitResult.Handled;
				}

				// the attribute already has a value ("Vertical|=\"2\"" - completing or fixing the casing of an
				// existing name): replace just the name, appending ="" would yield name=""="value"
				int followingCharPosition = span.End;
				while (followingCharPosition < nameSnapshot.Length && char.IsWhiteSpace (nameSnapshot[followingCharPosition])) {
					followingCharPosition++;
				}

				if (followingCharPosition < nameSnapshot.Length && nameSnapshot[followingCharPosition] == '=') {
					ReplaceSpanAndMoveCaret (session, buffer, span, item.InsertText, item.InsertText.Length);
					return CommitResult.Handled;
				}

				//FIXME get this from options
				char quoteChar = typedChar == '\'' ? '\'' : '"';

				// previously this code tried to insert the = and feed the " through the brace manager to get a
				// brace completion session, but that didn't 't work when inserting attributes into existing elements
				// as VS brace completion only works at the end of the line. now we have a XmlBraceCompletionCommandHandler
				// that implements custom overtype behaviors, and we can just insert the entire =""
				ReplaceSpanAndMoveCaret (session, buffer, span, $"{item.InsertText}={quoteChar}{quoteChar}", item.InsertText.Length + 2);
				// the quotes were inserted for the user: typing the quote consumes the opening one first, then overtypes the
				// closing one. a quote that committed the item counts as the opening quote already typed.
				var caret = session.TextView.Caret.Position.BufferPosition;
				if (caret.Position > 0 && caret.Position < caret.Snapshot.Length && caret.Snapshot[caret.Position - 1] == quoteChar && caret.Snapshot[caret.Position] == quoteChar) {
					XmlOvertypeSessions.Get (session.TextView).Start (
						new Span (caret.Position - 1, 1),
						new Span (caret.Position, 1),
						consumedPrefixLength: typedChar == quoteChar ? 1 : 0);
				}
				//explicitly trigger completion for the attribute value
				RetriggerCompletion (session.TextView);
				//if the user typed the quote char we're inserting, swallow it so they don't end up mismatched
				return typedChar == quoteChar? CommitSwallowChar : CommitResult.Handled;
			}
		case XmlCompletionItemKind.Element: {
				// elements can be made self-closing by committing with a /, but only if this does not cause the span to start with a /
				// otherwise that will prevent matching a closing tag item
				if (typedChar == '/') {
					var snapshot = buffer.CurrentSnapshot;
					var spanText = snapshot.GetText (span);
					if (span.Length == 0) {
						return CommitCancel;
					}
					var firstChar = snapshot[span.Start];
					if (span.Length == 1) {
						if (firstChar == '<' || firstChar == '/') {
							return CommitCancel;
						}
					} else {
						var secondChar = snapshot[span.Start + 1];
						if (firstChar == '/' || (firstChar == '<' && secondChar == '/')) {
							return CommitCancel;
						}
					}
				}
				ReplaceSpan (buffer, span, item.InsertText);
				return CommitResult.Handled;
			}
		case XmlCompletionItemKind.AttributeValue: {
				ReplaceSpan (buffer, span, item.InsertText);
				if (IsPathSeparator (typedChar) && CommitsOnPathSeparator (session)) {
					// the typed separator triggers completion for the next segment by itself;
					// a folder's insert text already ends with a separator, so don't type a second one
					bool endsWithSeparator = item.InsertText.Length > 0 && IsPathSeparator (item.InsertText[item.InsertText.Length - 1]);
					return endsWithSeparator ? CommitSwallowChar : CommitResult.Handled;
				}
				// e.g. a folder in a path: offer the next segment right away (unless the value was just closed by the typed quote)
				if (item.ShouldRetriggerCompletionOnCommit () && Array.IndexOf (attributeValueCommitChars, typedChar) < 0) {
					RetriggerCompletion (session.TextView);
				}
				return CommitResult.Handled;
			}
		case XmlCompletionItemKind.MultipleClosingTags:
		case XmlCompletionItemKind.ClosingTag: {
				InsertClosingTags (session, buffer, item);
				// the inserted closing tag already ends with >, so a > that committed it must not be typed too
				return typedChar == '>' ? CommitSwallowChar : CommitResult.Handled;
			}
		case XmlCompletionItemKind.Comment: {
				// this should probably be handled with brace matching and a separate undo step
				// but this is better than nothing
				ExtendSpanToConsume (ref span, '>');
				ReplaceSpanAndMoveCaret (session, buffer, span, item.InsertText + "-->", item.InsertText.Length);
				return CommitResult.Handled;
			}
		case XmlCompletionItemKind.CData: {
				// this should probably be handled with brace matching and a separate undo step
				// but this is better than nothing
				ExtendSpanToConsume (ref span, ']');
				ExtendSpanToConsume (ref span, ']');
				ExtendSpanToConsume (ref span, '>');
				ReplaceSpanAndMoveCaret (session, buffer, span, item.InsertText + "]]>", item.InsertText.Length);
				return CommitResult.Handled;
			}
		default:
			throw new InvalidOperationException ($"Unhandled XmlCompletionItemKind value '{itemKind}'");
		}
	}

	static void InsertClosingTags (IAsyncCompletionSession session, ITextBuffer buffer, CompletionItem item)
	{
		// completion may or may not include it depending how it was triggered
		bool includesBracket = item.InsertText[0] == '<';

		var insertTillName = item.InsertText.Substring (includesBracket? 2 : 1);
		var stack = item.Properties.GetProperty<List<XObject>> (typeof (List<XObject>));
		var elements = new List<XElement> ();
		for (int i = stack.Count - 1; i >= 0; i--) {
			if (stack[i] is XElement el) {
				elements.Add (el);
				if (el.Name.FullName == insertTillName) {
					break;
				}
			}
		}

		ITextSnapshot snapshot = buffer.CurrentSnapshot;
		var span = session.ApplicableToSpan.GetSpan (snapshot);

		// extend the span back to include the <, this logic assumes it's included
		if (!includesBracket) {
			span = new SnapshotSpan (span.Start - 1, span.Length + 1);
		}

		var thisLine = snapshot.GetLineFromPosition (span.Start);
		ITextSnapshotLine? prevLine = thisLine.LineNumber > 0? snapshot.GetLineFromLineNumber (thisLine.LineNumber - 1) : null;

		// if this completion is the first thing on the current line, reindent the current line
		var thisLineFirstNonWhitespaceOffset = thisLine.GetFirstNonWhitespaceOffset ();
		var replaceFirstIndent = thisLineFirstNonWhitespaceOffset.HasValue && thisLineFirstNonWhitespaceOffset + thisLine.Start == span.Start;
		if (replaceFirstIndent) {
			span = prevLine is not null
				?  new SnapshotSpan (prevLine.End, span.End)
				: new SnapshotSpan (thisLine.Start, span.End);
		}

		// for consistency, take the newline char from the beginning of this line if possible,
		// else from the end of this line, else from the options
		var newLine = prevLine?.GetLineBreakText () ?? thisLine.GetLineBreakText ();
		if (string.IsNullOrEmpty (newLine)) {
			newLine = session.TextView.Options.GetNewLineCharacter ();
		}

		var sb = new StringBuilder ();
		foreach (var element in elements) {
			var line = snapshot.GetLineFromPosition (element.Span.Start);
			// if the element we're closing was on a different line, and started that line,
			// then put the closing tag on a new line with indentation matching the opening tag
			if (line.LineNumber != thisLine.LineNumber && line.GetFirstNonWhitespaceOffset () is int nonWhitespaceOffset && (nonWhitespaceOffset + line.Start == element.Span.Start)) {
				var whitespaceSpan = new Span (line.Start, nonWhitespaceOffset);
				var whitespace = snapshot.GetText (whitespaceSpan);
				sb.Append (newLine);
				sb.Append (whitespace);
			}
			sb.Append ($"</{element.Name.FullName}>");
		}

		ExtendSpanToConsume (ref span, '>');

		var bufferEdit = buffer.CreateEdit ();
		bufferEdit.Replace (span, sb.ToString ());
		bufferEdit.Apply ();
	}
}