// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Text.Adornments;

namespace MonoDevelop.Xml.Editor.Completion
{
	public static class XmlImages
	{
		public static readonly Dictionary<string, int> TempImageIds = new Dictionary<string, int> ()
		{
			{ "XMLElement", 3245 },
			{ "XMLAttribute", 3335 },
		};
		public static readonly ImageElement Element = CreateElement (TempImageIds["XMLElement"]);
		public static readonly ImageElement Attribute = CreateElement (TempImageIds["XMLAttribute"]);
		public static readonly ImageElement AttributeValue = CreateElement (KnownImageIds.Constant);
		public static readonly ImageElement Namespace = CreateElement (KnownImageIds.XMLNamespace);
		public static readonly ImageElement Comment = CreateElement (KnownImageIds.XMLCommentTag);
		public static readonly ImageElement CData = CreateElement (KnownImageIds.XMLCDataTag);
		public static readonly ImageElement Prolog = CreateElement (KnownImageIds.XMLProcessInstructionTag);
		public static readonly ImageElement Entity = Prolog;
		public static ImageElement ClosingTag = Element;

		static readonly Guid KnownImagesGuid = KnownImageIds.ImageCatalogGuid;


		static ImageElement CreateElement (int id) =>
			new ImageElement (new ImageId (new Guid (KnownImageIds.ImageCatalogGuidString), 3335));
			//new ImageElement( new ImageId (new Guid ("ae27a6b0-e345-4288-96df-5eaf394ee369"), 3573));
			//new ImageElement (new ImageId (new System.Guid ("{ae27a6b0-e345-4288-96df-5eaf394ee369}"), 324));

	}
}
