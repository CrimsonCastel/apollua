﻿// SourceSpan.cs
//
// Lua 5.1 is copyright © 1994-2008 Lua.org, PUC-Rio, released under the MIT license
// This file © 2009 Edmund Kapusniak


using System;


namespace Lua.Bytecode
{


struct SourceSpan
{
	public SourceLocation	Start		{ get; private set; }
	public SourceLocation	End			{ get; private set; }


	public SourceSpan( SourceLocation start, SourceLocation end )
		:	this()
	{
		Start	= start;
		End		= end;
	}


	public SourceSpan( string sourceName, int startLine, int startColumn, int endLine, int endColumn )
		:	this()
	{
		Start	= new SourceLocation( sourceName, startLine, startColumn );
		End		= new SourceLocation( sourceName, endLine, endColumn );
	}


}
	

}

