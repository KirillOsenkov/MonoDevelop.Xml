// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System;
using System.ComponentModel.Composition;

using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;

using MonoDevelop.Xml.Editor.BraceCompletion;
using MonoDevelop.Xml.Editor.Logging;
using MonoDevelop.Xml.Logging;

namespace MonoDevelop.Xml.Editor.Overtype
{
	// Consumes / overtypes characters against the active XmlOvertypeSession, and lets Backspace remove an
	// auto-inserted pair (e.g. the quotes of an empty attribute value) in one go.
	//
	// Runs after the completion command handler, which chains first and only commits when the buffer changed
	// afterwards. So when the typed character would both overtype and commit the active completion session, we
	// chain instead of swallowing: completion rolls the character back, commits, and replays it, and the replay
	// then overtypes here (Helix #2938).
	[Name (Name)]
	[Export (typeof (ICommandHandler))]
	[Order (After = PredefinedCompletionNames.CompletionCommandHandler, Before = XmlBraceCompletionCommandHandler.Name)]
	[ContentType (XmlContentTypeNames.XmlCore)]
	[TextViewRole (PredefinedTextViewRoles.Interactive)]
	class XmlOvertypeCommandHandler : IChainedCommandHandler<TypeCharCommandArgs>, IChainedCommandHandler<BackspaceKeyCommandArgs>
	{
		public const string Name = nameof (XmlOvertypeCommandHandler);

		readonly IAsyncCompletionBroker completionBroker;
		readonly IEditorLoggerFactory loggerFactory;

		[ImportingConstructor]
		public XmlOvertypeCommandHandler (IAsyncCompletionBroker completionBroker, IEditorLoggerFactory loggerFactory)
		{
			this.completionBroker = completionBroker;
			this.loggerFactory = loggerFactory;
		}

		public string DisplayName => Name;

		public CommandState GetCommandState (TypeCharCommandArgs args, Func<CommandState> nextCommandHandler) => nextCommandHandler ();

		public CommandState GetCommandState (BackspaceKeyCommandArgs args, Func<CommandState> nextCommandHandler) => nextCommandHandler ();

		public void ExecuteCommand (TypeCharCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
		{
			try {
				if (TryHandleTypeChar (args, nextCommandHandler, executionContext)) {
					return;
				}
			} catch (Exception ex) {
				loggerFactory.GetLogger<XmlOvertypeCommandHandler> (args.TextView).LogInternalException (ex);
			}

			nextCommandHandler ();
		}

		bool TryHandleTypeChar (TypeCharCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
		{
			var view = args.TextView;
			if (!view.Selection.IsEmpty || view.GetMultiSelectionBroker ().HasMultipleSelections) {
				return false;
			}

			var sessions = XmlOvertypeSessions.Get (view);
			var session = sessions.Active;
			if (session == null) {
				return false;
			}

			if (!session.WouldHandle (view, args.TypedChar)) {
				sessions.TryHandleTypedChar (args.TypedChar);
				return false;
			}

			var completionSession = completionBroker.GetSession (view);
			if (completionSession != null && !completionSession.IsDismissed
				&& completionSession.ShouldCommit (args.TypedChar, view.Caret.Position.BufferPosition, executionContext.OperationContext.UserCancellationToken)) {
				return false;
			}

			return sessions.TryHandleTypedChar (args.TypedChar);
		}

		public void ExecuteCommand (BackspaceKeyCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
		{
			try {
				if (TryDeleteEmptyPair (args)) {
					return;
				}
			} catch (Exception ex) {
				loggerFactory.GetLogger<XmlOvertypeCommandHandler> (args.TextView).LogInternalException (ex);
			}

			nextCommandHandler ();
		}

		bool TryDeleteEmptyPair (BackspaceKeyCommandArgs args)
		{
			var view = args.TextView;
			if (!view.Selection.IsEmpty || view.GetMultiSelectionBroker ().HasMultipleSelections) {
				return false;
			}

			var sessions = XmlOvertypeSessions.Get (view);
			var session = sessions.Active;
			if (session == null) {
				return false;
			}

			var caret = view.Caret.Position.BufferPosition;
			var snapshot = caret.Snapshot;
			var prefix = session.Prefix.GetSpan (snapshot);
			var suffix = session.Suffix.GetSpan (snapshot);
			if (prefix.Length == 0 || suffix.Length == 0 || prefix.End != caret.Position || suffix.Start != caret.Position) {
				return false;
			}

			sessions.End (session);
			view.TextBuffer.Delete (new Span (prefix.Start, prefix.Length + suffix.Length));
			return true;
		}
	}
}
