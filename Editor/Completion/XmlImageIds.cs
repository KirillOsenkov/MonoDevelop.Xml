// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Text.Adornments;

namespace MonoDevelop.Xml.Editor.Completion
{
	public static class XmlImages
	{
		// Warning: the order of these static fields is important,
		// declare in the order they depend on one another.
		// If this Guid is at the end, the other fields will read the empty value before it gets initialized
		static readonly Guid KnownImagesGuid = KnownImageIds.ImageCatalogGuid;

		static ImageElement CreateElement (int id) => new ImageElement (new ImageId (KnownImagesGuid, id));

		public static readonly ImageElement Element = CreateElement (3245);
		public static readonly ImageElement Attribute = CreateElement (3335);
		public static readonly ImageElement AttributeValue = CreateElement (KnownImageIds.Constant);
		public static readonly ImageElement Namespace = CreateElement (KnownImageIds.XMLNamespace);
		public static readonly ImageElement Comment = CreateElement (KnownImageIds.XMLCommentTag);
		public static readonly ImageElement CData = CreateElement (KnownImageIds.XMLCDataTag);
		public static readonly ImageElement Prolog = CreateElement (KnownImageIds.XMLProcessInstructionTag);
		public static readonly ImageElement Entity = Prolog;
		public static ImageElement ClosingTag = Element;
	}
}
