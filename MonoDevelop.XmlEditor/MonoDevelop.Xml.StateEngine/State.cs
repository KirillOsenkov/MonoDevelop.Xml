// 
// State.cs
// 
// Author:
//   Mikayla Hutchinson <m.j.hutchinson@gmail.com>
// 
// Copyright (C) 2008 Novell, Inc (http://www.novell.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.Text;

namespace MonoDevelop.Xml.StateEngine
{
	public abstract class State
	{
		State parent;
		int startLocation;
		int endLocation = -1;

		public State (State parent, int startLocation)
		{
			System.Diagnostics.Debug.Assert (parent != null || startLocation == 0);
			System.Diagnostics.Debug.Assert (startLocation >= parent.StartLocation);
			this.parent = parent;
			this.startLocation = startLocation;
		}

		/// <summary>
		/// When the <see cref="Parser"/> advances by one character, it calls this method 
		/// on the currently active <see cref="State"/> to determine the next state.
		/// </summary>
		/// <param name="c">The current character.</param>
		/// <param name="reject"> If true, the character is being rejected, and should be 
		/// passed on to the next state.</param>
		/// <returns>
		/// The next state. A new or parent <see cref="State"/> will change the parser state; 
		/// the current state or <see cref="null"/> will not.
		/// </returns>
		public abstract State PushChar (char c, int location, out bool reject);

		public State Parent {
			get { return parent; }
		}

		public int StartLocation {
			get { return startLocation; }
		}

		public int EndLocation
		{
			get { return endLocation; }
		}
		
		public IEnumerable<State> ParentStack
		{
			get {
				State p = Parent;
				while (p != null) {
					yield return p;
					p = p.Parent;
				}
			}
		}

		protected void Close (int endLocation)
		{
#if DEBUG
			if (this.endLocation != -1)
				throw new InvalidOperationException ("The State has already been closed.");
			if (endLocation < startLocation)
				throw new InvalidOperationException ("The State cannot end before it starts.");
#endif
			this.endLocation = endLocation;
		}
		
		#region Cloning API
		
		public State StackCopy ()
		{
			State copy = ShallowCopy ();
			if (parent != null)
				copy.parent = parent.StackCopy ();
			
			//check that the subclass has implemented the API fully
			System.Diagnostics.Debug.Assert (copy.GetType () == this.GetType ());
			System.Diagnostics.Debug.Assert (copy.startLocation == this.startLocation);
			
			return copy;
		}
		
		//this should simply call the protected cloning constructor chain
		public abstract State ShallowCopy ();
		
		protected State (State copyFrom)
		{
			startLocation = copyFrom.startLocation;
			endLocation = copyFrom.endLocation;
		}
		
		#endregion
	}
}
