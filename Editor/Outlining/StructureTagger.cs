// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Tagging;

using MonoDevelop.Xml.Dom;
using MonoDevelop.Xml.Editor.Parsing;
using MonoDevelop.Xml.Logging;

namespace MonoDevelop.Xml.Editor.Tagging
{
	class StructureTagger : ITagger<IStructureTag>
	{
		readonly ITextBuffer buffer;
		readonly ILogger logger;
		private readonly XmlParserProvider xmlParserProvider;
		static readonly IEnumerable<ITagSpan<IStructureTag>> emptyTagList = Array.Empty<ITagSpan<IStructureTag>> ();

		public StructureTagger (ITextBuffer buffer, ILogger logger, StructureTaggerProvider provider)
		{
			this.buffer = buffer;
			this.logger = logger;
			xmlParserProvider = provider.ParserProvider;
		}

		private XmlBackgroundParser? parser;
		private XmlBackgroundParser Parser
		{
			get
			{
				if (parser == null)
				{
					parser = xmlParserProvider.GetParser(buffer);
				}

				return parser;
			}
		}

		XmlParseResult? lastResult;
		ITextSnapshot? lastResultSnapshot;

		public event EventHandler<SnapshotSpanEventArgs>? TagsChanged;

		public IEnumerable<ITagSpan<IStructureTag>> GetTags (NormalizedSnapshotSpanCollection spans)
			=> logger.InvokeAndLogExceptions (() => GetTagsInternal (spans));

		IEnumerable<ITagSpan<IStructureTag>> GetTagsInternal (NormalizedSnapshotSpanCollection spans)
		{
			if (spans.Count == 0) {
				return emptyTagList;
			}

			var snapshot = spans[0].Snapshot;

			if (snapshot.Length > 5_000_000)
			{
				return emptyTagList;
			}

			var parseTask = Parser.GetOrProcessAsync (snapshot, default);

			if (parseTask.IsCompleted) {
				#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
				lastResult = parseTask.Result;
				#pragma warning restore VSTHRD002
				lastResultSnapshot = snapshot;
			} else {
				parseTask.ContinueWith (t => {
					if (t.Status == TaskStatus.RanToCompletion) {
						lastResult = t.Result;
						lastResultSnapshot = snapshot;
					}
					RaiseTagsChanged ();
				}, TaskScheduler.Default).LogTaskExceptionsAndForget (logger);
			}

			// While a parse for a newer snapshot is still running, keep serving the tags from
			// the last completed parse, translated forward. Returning an empty list here makes
			// the outlining manager treat all collapsed regions in the queried spans as removed
			// and expand them.
			if (lastResult is XmlParseResult result && lastResultSnapshot is ITextSnapshot parsedSnapshot) {
				var parsedSpans = parsedSnapshot == snapshot
					? spans
					: new NormalizedSnapshotSpanCollection (new SnapshotSpan (parsedSnapshot, 0, parsedSnapshot.Length));
				return GetTags (result, parsedSpans, parsedSnapshot, snapshot);
			}

			return emptyTagList;
		}

		private void RaiseTagsChanged ()
		{
			var snapshot = buffer.CurrentSnapshot;
			var args = new SnapshotSpanEventArgs (new SnapshotSpan (snapshot, 0, snapshot.Length));
			TagsChanged?.Invoke (this, args);
		}

		private IEnumerable<ITagSpan<IStructureTag>> GetTags (XmlParseResult xmlParseResult, NormalizedSnapshotSpanCollection spans, ITextSnapshot snapshot, ITextSnapshot querySnapshot)
		{
			var root = xmlParseResult.XDocument;

			var resultList = new List<ITagSpan<IStructureTag>> ();

			int previousLineStart = -1;
			int previousLineEnd = -1;

			foreach (var snapshotSpan in spans) {
				var nodes = GetNodesIntersectingRange (root, new TextSpan (snapshotSpan.Span.Start, snapshotSpan.Span.Length));
				foreach (var node in nodes) {
					// exclude the document itself since the root element will take care of it
					if (node is XDocument) {
						continue;
					}

					// no need to collapse text as usually collapsing the parent is good enough
					if (node is XText) {
						continue;
					}

					var nodeSpan = node.OuterSpan;
					if (nodeSpan.Start < 0 || nodeSpan.End > snapshot.Length) {
						continue;
					}

					var outliningSpan = new Span (nodeSpan.Start, nodeSpan.Length);
					var startLine = snapshot.GetLineFromPosition (outliningSpan.Start);
					var endLine = snapshot.GetLineFromPosition (outliningSpan.End);
					if (startLine.LineNumber == endLine.LineNumber) {
						// ignore single-line nodes 
						continue;
					}

					if (startLine.LineNumber == previousLineStart || endLine.LineNumber == previousLineEnd) {
						// ignore multiple nodes starting and ending on the same line
						continue;
					}

					previousLineStart = startLine.LineNumber;
					previousLineEnd = endLine.LineNumber;

					var headerSpan = new Span (outliningSpan.Start, Math.Min (startLine.End.Position - outliningSpan.Start, 100));
					string firstLine = snapshot.GetText (headerSpan);
					string collapseForm = firstLine;

					var tagSnapshotSpan = new SnapshotSpan (snapshot, outliningSpan)
						.TranslateTo (querySnapshot, SpanTrackingMode.EdgeExclusive);
					var headerSnapshotSpan = new SnapshotSpan (snapshot, headerSpan)
						.TranslateTo (querySnapshot, SpanTrackingMode.EdgeExclusive);
					var structureTag = new StructureTag (
						querySnapshot,
						outliningSpan: tagSnapshotSpan.Span,
						headerSpan: headerSnapshotSpan.Span,
						guideLineSpan: tagSnapshotSpan.Span,
						guideLineHorizontalAnchor: tagSnapshotSpan.Span.Start,
						type: PredefinedStructureTagTypes.Structural,
						isCollapsible: true,
						collapsedForm: collapseForm,
						collapsedHintForm: snapshot.GetText (headerSpan));
					var tagSpan = new TagSpan<IStructureTag> (tagSnapshotSpan, structureTag);
					resultList.Add (tagSpan);
				}
			}

			return resultList;
		}

		private static IEnumerable<XNode> GetNodesIntersectingRange (XContainer root, TextSpan span)
		{
			if (root.OuterSpan.Intersects (span)) {
				yield return root;
			}

			foreach (var child in root.Nodes) {
				if (child.OuterSpan.End < span.Start) {
					continue;
				}

				if (child.OuterSpan.Start >= span.End) {
					break;
				}

				if (child is XContainer childContainer) {
					foreach (var grandchild in GetNodesIntersectingRange (childContainer, span)) {
						yield return grandchild;
					}
				} else {
					yield return child;
				}
			}
		}
	}
}