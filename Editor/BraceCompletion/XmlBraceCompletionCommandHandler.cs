// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.ComponentModel.Composition;
using System.Threading;

using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text;
//using Microsoft.VisualStudio.Text.BraceCompletion;
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

using BF = System.Reflection.BindingFlags;

namespace MonoDevelop.Xml.Editor.BraceCompletion
{
	// The VS editor's brace completion infrastructure has limitations that we cannot work around, so instead of
	// implementing a brace completion context/session provider, we implement custom XML brace insertions and overtype behaviors
	//
	// the main limitation is that brace completion can only happen at the end of a line, or when the only remaining characters
	// on the line are part of active brace completion sessions. this means that you cannot get quote completion when typing an
	// attribute into an existing element.
	//
	// another limitation is that brace completion handlers from base content types are ignored. content types derived from
	// XmlCore (such as MSBuild) cannot implement their own brace completion behaviors while still inheriting XmlCore behaviors
	//
	// it's also difficult for commit handlers to create completion sessions. for example, when an attribute is committed
	// the commit handler may insert ="" quotes and move the caret between the quotes. it's not possible to create a brace
	// completion session so that the second quote becomes overtypeable.
	//
	// the one big downside to this is that we lose the little bit of editor UI that indicates that the character is
	// overtypeable. reimplementing is is nontrivial, especially across multiple platforms.
	//
	[Name (Name)]
	[Export (typeof (ICommandHandler))]
	[Order (After = PredefinedCompletionNames.CompletionCommandHandler, Before = "BraceCompletionCommandHandler")]
	[ContentType (XmlContentTypeNames.XmlCore)]
	[TextViewRole (PredefinedTextViewRoles.Interactive)]
	class XmlBraceCompletionCommandHandler : IChainedCommandHandler<TypeCharCommandArgs>
	{
		public const string Name = nameof (XmlBraceCompletionCommandHandler);

		[ImportingConstructor]
		public XmlBraceCompletionCommandHandler (XmlParserProvider parserProvider, IEditorLoggerFactory loggerFactory, IAsyncCompletionBroker completionBroker)
		{
			this.parserProvider = parserProvider;
			this.loggerFactory = loggerFactory;
			this.completionBroker = completionBroker;
		}

		readonly XmlParserProvider parserProvider;
		readonly IEditorLoggerFactory loggerFactory;
		readonly IAsyncCompletionBroker completionBroker;

		// log
		public string DisplayName => Name;

		public CommandState GetCommandState (TypeCharCommandArgs args, Func<CommandState> nextCommandHandler) => nextCommandHandler ();

		public void ExecuteCommand (TypeCharCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
		{
			try {
				ExecuteCommandInternal (args, nextCommandHandler, executionContext);
			} catch (Exception ex) {
				loggerFactory.GetLogger<XmlBraceCompletionCommandHandler> (args.TextView).LogInternalException (ex);
			}
		}

		void ExecuteCommandInternal (TypeCharCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
		{
			var view = args.TextView;
			var buffer = args.SubjectBuffer;
			var openingPoint = view.Caret.Position.BufferPosition;
			char typedChar = args.TypedChar;
			var token = executionContext.OperationContext.UserCancellationToken;

			// short circuit on the easy checks
			ITextSnapshot snapshot = openingPoint.Snapshot;
			if (!IsTriggerChar (typedChar)
				|| !IsBraceCompletionEnabled (view)
				|| !view.Selection.IsEmpty
				|| !parserProvider.TryGetParser (snapshot.TextBuffer, out var parser)) {
				nextCommandHandler ();
				return;
			}

			// fallback overtype of the closing quote of an attribute value when no XmlOvertypeSession applies
			// (e.g. retyping the value of an existing attribute); auto-inserted quotes are handled by XmlOvertypeCommandHandler
			if (IsQuoteChar(typedChar) && openingPoint > 0 && snapshot.Length > openingPoint && snapshot[openingPoint] == typedChar) {
				var spine = parser.GetSpineParser (openingPoint, token);
				if (spine.GetAttributeValueDelimiter () == typedChar) {
					// the completion command handler runs before us and only commits if the buffer changed after chaining.
					// if the quote is a commit char for the active session, let it through: completion rolls it back,
					// commits, and replays the key, which then overtypes the closing quote here (Helix #2938)
					var completionSession = completionBroker.GetSession (view);
					if (completionSession != null && !completionSession.IsDismissed && completionSession.ShouldCommit (typedChar, openingPoint, token)) {
						nextCommandHandler ();
						return;
					}

					view.Caret.MoveTo (new SnapshotPoint (buffer.CurrentSnapshot, openingPoint + 1));
					return;
				}
			}

			nextCommandHandler ();

			if (typedChar == '=') {
				var position = view.Caret.Position.BufferPosition;
				snapshot = position.Snapshot;
				if (position > 0 &&
					snapshot[position - 1] == '=' &&
					(position == snapshot.Length ||
						(position < snapshot.Length &&
						snapshot[position] is char next &&
						(next == ' ' || next == '>' || next == '/' || next == '\r' || next == '\n')))) {
					InsertDoubleQuotes ("\"\"", position);
				}
			}

			return;

			void InsertDoubleQuotes(string doubleQuotes, SnapshotPoint openingPoint)
			{
				var spine = parser.GetSpineParser (openingPoint);
				if (spine.IsExpectingAttributeQuote ()) {
					//TODO create an undo transition between the two chars
					buffer.Insert (openingPoint, doubleQuotes);
					view.Caret.MoveTo (new SnapshotPoint (buffer.CurrentSnapshot, openingPoint.Position + 1));
					// both quotes were inserted for the user: the first typed quote is the opening one, the second overtypes the closing one
					XmlOvertypeSessions.Get (view).Start (new Span (openingPoint.Position, 1), new Span (openingPoint.Position + 1, 1));
					return;
				}
			}
		}

		static bool IsBraceCompletionEnabled (ITextView textView)
		//=> textView.Properties.TryGetProperty ("BraceCompletionManager", out IBraceCompletionManager manager) && manager.Enabled;
		//HACK: VSMac as of 16.4 doesn't have IBraceCompletionManager in the assembly that the 16.4 nugets
		// say it's in, so we can't use it even when depending on 16.4. use reflection instead.
		{
			if (!textView.Options.GetAutoInsertAttributeValue())
			{
				return false;
			}

			return true; // default to true if can't find BraceCompletionManager
		}

		static System.Reflection.PropertyInfo braceManagerEnabledProp;

		static bool IsQuoteChar (char ch) => ch == '"' || ch == '\'';
		static bool IsTriggerChar(char ch) => IsQuoteChar(ch) || ch == '=';
	}
}
