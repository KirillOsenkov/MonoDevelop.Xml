// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;

namespace MonoDevelop.Xml.Editor.Completion
{
	//FIXME: replace this MD shim with something better
	internal class LoggingService
	{
		[Conditional("DEBUG")]
		internal static void LogWarning (string formatString, params object[] args) => Console.WriteLine (formatString, args);

		[Conditional("DEBUG")]
		internal static void LogDebug (string formatString, params object[] args) => Console.WriteLine (formatString, args);
	}
}
