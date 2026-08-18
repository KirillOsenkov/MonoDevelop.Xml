// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System;
using System.Collections.Generic;

using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace MonoDevelop.Xml.Editor.Overtype
{
	/// <summary>
	/// The overtype sessions of one text view. The most recently started valid session is the active one.
	/// <see cref="Changed"/> fires whenever a session starts, consumes a character or ends, so a presenter
	/// can underline the text that can still be typed over.
	/// </summary>
	public sealed class XmlOvertypeSessions
	{
		readonly ITextView textView;
		readonly List<XmlOvertypeSession> sessions = new ();

		XmlOvertypeSessions (ITextView textView)
		{
			this.textView = textView;
			textView.Caret.PositionChanged += (s, e) => Cleanup ();
			textView.TextBuffer.PostChanged += (s, e) => Cleanup ();
			textView.Closed += (s, e) => sessions.Clear ();
		}

		public static XmlOvertypeSessions Get (ITextView textView)
			=> textView.Properties.GetOrCreateSingletonProperty (typeof (XmlOvertypeSessions), () => new XmlOvertypeSessions (textView));

		public event EventHandler? Changed;

		public XmlOvertypeSession? Active {
			get {
				Cleanup ();
				return sessions.Count > 0 ? sessions[sessions.Count - 1] : null;
			}
		}

		/// <summary>
		/// Starts a session for text just inserted around the caret: <paramref name="prefix"/> must end at the caret
		/// and <paramref name="suffix"/> must start at it. Returns null when nothing was started (multiple selections,
		/// both spans empty, or the caret is not between them).
		/// </summary>
		public XmlOvertypeSession? Start (Span prefix, Span suffix, int consumedPrefixLength = 0)
		{
			if (textView.GetMultiSelectionBroker ().HasMultipleSelections) {
				return null;
			}

			var caret = textView.Caret.Position.BufferPosition;
			if (prefix.Length == 0 && suffix.Length == 0) {
				return null;
			}

			if (prefix.End != caret.Position || suffix.Start != caret.Position) {
				return null;
			}

			if (suffix.End > caret.Snapshot.Length) {
				return null;
			}

			var session = new XmlOvertypeSession (caret.Snapshot, prefix, suffix, consumedPrefixLength);
			sessions.Add (session);
			Changed?.Invoke (this, EventArgs.Empty);
			return session;
		}

		public XmlOvertypeSession? StartPrefix (Span prefix, int consumedPrefixLength = 0)
			=> Start (prefix, new Span (prefix.End, 0), consumedPrefixLength);

		internal bool TryHandleTypedChar (char typedChar)
		{
			var session = Active;
			if (session == null) {
				return false;
			}

			bool handled = session.TryHandle (textView, typedChar);
			Cleanup ();
			Changed?.Invoke (this, EventArgs.Empty);
			return handled;
		}

		internal void End (XmlOvertypeSession session)
		{
			if (sessions.Remove (session)) {
				Changed?.Invoke (this, EventArgs.Empty);
			}
		}

		void Cleanup ()
		{
			bool removed = false;
			for (int i = sessions.Count - 1; i >= 0; i--) {
				if (!sessions[i].IsValid (textView)) {
					sessions.RemoveAt (i);
					removed = true;
				}
			}

			if (removed) {
				Changed?.Invoke (this, EventArgs.Empty);
			}
		}
	}
}
