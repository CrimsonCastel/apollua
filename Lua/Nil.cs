﻿// Nil.cs
//
// Lua 5.1 is copyright © 1994-2008 Lua.org, PUC-Rio, released under the MIT license
// LuaCLR is copyright © 2007-2008 Fabio Mascarenhas, released under the MIT license
// Modifications copyright © 2009 Edmund Kapusniak


using System;
using System.Diagnostics;


namespace Lua
{


[DebuggerDisplay( "nil" )]
public sealed class Nil
	:	Value
{

	// Singleton instance.

	public static Nil Instance { get; private set; }

	static Nil()
	{
		Instance = new Nil();
	}

	private Nil()
	{
	}
	


	// Hashing.
	
	public override string ToString()
	{
		return "nil";
	}



	// Comparison operators.

	public override bool IsTrue()
	{
		return false;
	}

}


}

