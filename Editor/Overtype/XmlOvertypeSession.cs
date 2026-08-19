// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System;

using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace MonoDevelop.Xml.Editor.Overtype
{
	/// <summary>
	/// Text the XML editor inserted on the user's behalf that the user may still type by hand:
	/// the <see cref="Prefix"/> sits immediately before the caret and is consumed in order by typing its
	/// characters (each typed character is swallowed and the caret does not move); the <see cref="Suffix"/>
	/// sits immediately after the caret and is overtyped in order (each typed character moves the caret over it).
	/// Either may be empty. Examples: the quotes inserted for an attribute value (prefix <c>"</c>, suffix <c>"</c>),
	/// the <c>&gt;</c> inserted after <c>/</c> (prefix only), the <c>name&gt;</c> inserted after <c>&lt;/</c> (prefix only).
	/// The session ends when the caret leaves the region between the end of the prefix and the end of the suffix,
	/// when either text changes, or when everything has been consumed. Typing any character that is neither
	/// consumed nor overtyped makes the prefix permanent, as does any text that ends up between the prefix and
	/// the suffix by other means (a completion commit, a paste); the suffix stays overtypeable.
	/// Same rules as the VS editor's own brace-completion overtype session, minus the requirement that the
	/// prefix and suffix be identical for the prefix to be consumable.
	/// </summary>
	public sealed class XmlOvertypeSession
	{
		readonly string prefixText;
		readonly string suffixText;
		bool canConsumePrefix = true;

		internal XmlOvertypeSession (ITextSnapshot snapshot, Span prefix, Span suffix, int consumedPrefixLength)
		{
			Prefix = snapshot.CreateTrackingSpan (prefix, SpanTrackingMode.EdgeExclusive);
			Suffix = snapshot.CreateTrackingSpan (suffix, SpanTrackingMode.EdgeExclusive);
			prefixText = snapshot.GetText (prefix);
			suffixText = snapshot.GetText (suffix);
			ConsumedPrefixLength = Math.Min (consumedPrefixLength, prefixText.Length);
		}

		public ITrackingSpan Prefix { get; }

		public ITrackingSpan Suffix { get; }

		public int ConsumedPrefixLength { get; private set; }

		public int OvertypedSuffixLength { get; private set; }

		/// <summary>False once the user typed something that was neither consumed nor overtyped: the prefix is then permanent.</summary>
		public bool CanConsumePrefix => canConsumePrefix;

		/// <summary>The part of the prefix the user has not typed yet (empty once the prefix is permanent).</summary>
		public SnapshotSpan RemainingPrefix (ITextSnapshot snapshot)
		{
			var span = Prefix.GetSpan (snapshot);
			if (!canConsumePrefix) {
				return new SnapshotSpan (snapshot, span.End, 0);
			}

			int consumed = Math.Min (ConsumedPrefixLength, span.Length);
			return new SnapshotSpan (snapshot, span.Start + consumed, span.Length - consumed);
		}

		/// <summary>The part of the suffix the user has not typed yet.</summary>
		public SnapshotSpan RemainingSuffix (ITextSnapshot snapshot)
		{
			var span = Suffix.GetSpan (snapshot);
			int overtyped = Math.Min (OvertypedSuffixLength, span.Length);
			return new SnapshotSpan (snapshot, span.Start + overtyped, span.Length - overtyped);
		}

		public bool IsValid (ITextView textView)
		{
			var caret = textView.Caret.Position.BufferPosition;
			var snapshot = caret.Snapshot;
			if (snapshot.TextBuffer != Prefix.TextBuffer) {
				return false;
			}

			var prefix = Prefix.GetSpan (snapshot);
			var suffix = Suffix.GetSpan (snapshot);
			if (snapshot.GetText (prefix) != prefixText || snapshot.GetText (suffix) != suffixText) {
				return false;
			}

			// anything inserted between the prefix and the suffix (e.g. a completion commit)
			// means the prefix can no longer be typed by hand: it becomes permanent
			if (canConsumePrefix && prefix.End != suffix.Start) {
				canConsumePrefix = false;
			}

			if (suffix.Length == 0) {
				return caret.Position == prefix.End && canConsumePrefix && ConsumedPrefixLength < prefixText.Length;
			}

			if (caret.Position < prefix.End || caret.Position >= suffix.End) {
				return false;
			}

			if (caret.Position > suffix.Start && caret.Position != suffix.Start + OvertypedSuffixLength) {
				return false;
			}

			return true;
		}

		/// <summary>
		/// True when typing <paramref name="typedChar"/> now would be consumed by the prefix or overtype the suffix.
		/// </summary>
		public bool WouldHandle (ITextView textView, char typedChar)
		{
			var caret = textView.Caret.Position.BufferPosition;
			var snapshot = caret.Snapshot;
			var prefix = Prefix.GetSpan (snapshot);
			if (canConsumePrefix && caret.Position == prefix.End && ConsumedPrefixLength < prefixText.Length && prefixText[ConsumedPrefixLength] == typedChar) {
				return true;
			}

			var suffix = Suffix.GetSpan (snapshot);
			return OvertypedSuffixLength < suffixText.Length
				&& caret.Position == suffix.Start + OvertypedSuffixLength
				&& suffixText[OvertypedSuffixLength] == typedChar;
		}

		/// <summary>
		/// Consumes the character into the prefix or overtypes it in the suffix. Returns false (and makes the
		/// prefix permanent) when the character matches neither, in which case the caller types it normally.
		/// </summary>
		internal bool TryHandle (ITextView textView, char typedChar)
		{
			var caret = textView.Caret.Position.BufferPosition;
			var snapshot = caret.Snapshot;
			var prefix = Prefix.GetSpan (snapshot);
			if (canConsumePrefix && caret.Position == prefix.End && ConsumedPrefixLength < prefixText.Length && prefixText[ConsumedPrefixLength] == typedChar) {
				ConsumedPrefixLength++;
				return true;
			}

			canConsumePrefix = false;

			var suffix = Suffix.GetSpan (snapshot);
			if (OvertypedSuffixLength < suffixText.Length
				&& caret.Position == suffix.Start + OvertypedSuffixLength
				&& suffixText[OvertypedSuffixLength] == typedChar) {
				OvertypedSuffixLength++;
				textView.Caret.MoveTo (new SnapshotPoint (snapshot, caret.Position + 1));
				textView.Selection.Clear ();
				return true;
			}

			return false;
		}
	}
}
