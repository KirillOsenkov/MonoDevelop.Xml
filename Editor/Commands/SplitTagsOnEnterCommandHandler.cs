// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;

using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Text.Editor.OptionsExtensionMethods;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;

using MonoDevelop.Xml.Editor.Logging;
using MonoDevelop.Xml.Logging;

namespace MonoDevelop.Xml.Editor.Commands
{
	[Name (Name)]
	[Export (typeof (ICommandHandler))]
	[Order (Before = PredefinedCompletionNames.CompletionCommandHandler)]
	[ContentType(XmlContentTypeNames.XmlCore)]
	[TextViewRole (PredefinedTextViewRoles.Interactive)]
	class SplitTagsOnEnterCommandHandler : IChainedCommandHandler<ReturnKeyCommandArgs>
	{
		readonly IEditorLoggerFactory loggerFactory;
		readonly ISmartIndentationService smartIndentService;
		readonly ITextBufferUndoManagerProvider undoManagerProvider;
		readonly IAsyncCompletionBroker completionBroker;

		[ImportingConstructor]
		public SplitTagsOnEnterCommandHandler (
			IEditorLoggerFactory loggerFactory, ISmartIndentationService smartIndentService,
			ITextBufferUndoManagerProvider undoManagerProvider, IAsyncCompletionBroker completionBroker)
		{
			this.loggerFactory = loggerFactory;
			this.smartIndentService = smartIndentService;
			this.undoManagerProvider = undoManagerProvider;
			this.completionBroker = completionBroker;
		}

		const string Name = nameof (SplitTagsOnEnterCommandHandler);

		public string DisplayName => Name;

		public void ExecuteCommand (ReturnKeyCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
		{
			if (smartIndentService == null || completionBroker.IsCompletionActive (args.TextView)) {
				nextCommandHandler ();
				return;
			}

			try {
				ExecuteCommandInternal (args, nextCommandHandler, executionContext);
			} catch (Exception ex) {
				loggerFactory.GetLogger<SplitTagsOnEnterCommandHandler> (args.TextView).LogInternalException (ex);
				throw;
			}
		}

		void ExecuteCommandInternal (ReturnKeyCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
		{
			// we could be smarter about actually analyzing the XML and creating good undo transactions.
			// right now it just looks for carets being between "></" and moves the rest of the line
			// onto an indented new line before letting the normal handler run.
			// every caret of a multi-selection is handled, then the carets are put back where they were
			// so that the normal handler inserts its newline at each of them
			var broker = args.TextView.GetMultiSelectionBroker ();
			var selections = broker.AllSelections;
			var s = args.SubjectBuffer.CurrentSnapshot;

			var splitPositions = new List<int> ();
			foreach (var selection in selections) {
				int p = selection.InsertionPoint.Position.Position;
				bool betweenTags = p > 0 && (p + 3) < s.Length && s[p - 1] == '>' && s[p] == '<' && s[p + 1] == '/';
				if (betweenTags && !splitPositions.Contains (p)) {
					splitPositions.Add (p);
				}
			}

			if (splitPositions.Count == 0) {
				nextCommandHandler ();
				return;
			}

			splitPositions.Sort ();

			// how much text was inserted at each split position (line break plus indent), for remapping the carets
			var insertedLengths = new int[splitPositions.Count];

			var undoManager = undoManagerProvider.GetTextBufferUndoManager (args.SubjectBuffer);
			using (var transaction = undoManager.TextBufferUndoHistory.CreateTransaction ("Split Tags")) {
				string lineBreakText = null;
				if (args.TextView.Options.GetReplicateNewLineCharacter ()) {
					lineBreakText = s.GetLineFromPosition (splitPositions[0]).GetLineBreakText ();
				}
				if (string.IsNullOrEmpty (lineBreakText)) {
					lineBreakText = args.TextView.Options.GetNewLineCharacter ();
				}

				var edit = args.SubjectBuffer.CreateEdit ();
				foreach (int p in splitPositions) {
					edit.Insert (p, lineBreakText);
				}
				s = edit.Apply ();

				// the new lines start after the line break; earlier splits shift the later ones
				edit = args.SubjectBuffer.CreateEdit ();
				int shift = 0;
				for (int i = 0; i < splitPositions.Count; i++) {
					insertedLengths[i] = lineBreakText.Length;
					var nextLine = s.GetLineFromPosition (splitPositions[i] + shift + lineBreakText.Length);
					var indent = smartIndentService.GetDesiredIndentation (args.TextView, nextLine);
					if (indent != null) {
						string indentString = GetIndentString (indent.Value, args.TextView.Options);
						edit.Insert (nextLine.Start.Position, indentString);
						insertedLengths[i] += indentString.Length;
					}
					shift += lineBreakText.Length;
				}
				s = edit.Apply ();
				transaction.Complete ();
			}

			// move the carets back to their original locations before letting the rest of the handlers run
			var newSelections = new List<Selection> (selections.Count);
			int primaryIndex = 0;
			for (int i = 0; i < selections.Count; i++) {
				var selection = selections[i];
				if (selection == broker.PrimarySelection) {
					primaryIndex = i;
				}

				int p = selection.InsertionPoint.Position.Position;
				int newPosition = p;
				for (int j = 0; j < splitPositions.Count && splitPositions[j] < p; j++) {
					newPosition += insertedLengths[j];
				}

				newSelections.Add (new Selection (new VirtualSnapshotPoint (new SnapshotPoint (s, newPosition))));
			}

			broker.SetSelectionRange (newSelections, newSelections[primaryIndex]);

			nextCommandHandler ();
		}

		static string GetIndentString (int indent, IEditorOptions options)
		{
			if (options.IsConvertTabsToSpacesEnabled ()) {
				return new string (' ', indent);
			}
			var tabSize = options.GetTabSize ();
			int tabs = indent / tabSize;
			int spaces = indent - tabs * tabSize;
			var indentStr = new string ('\t', tabs);
			if (spaces > 0) {
				indentStr += new string (' ', spaces);
			}
			return indentStr;
		}

		public CommandState GetCommandState (ReturnKeyCommandArgs args, Func<CommandState> nextCommandHandler)
			=> nextCommandHandler ();
	}
}
