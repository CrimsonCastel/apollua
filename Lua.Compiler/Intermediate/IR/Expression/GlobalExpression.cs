// GlobalExpression.cs
//
// Lua 5.1 is copyright � 1994-2008 Lua.org, PUC-Rio, released under the MIT license
// LuaCLR is copyright � 2007-2008 Fabio Mascarenhas, released under the MIT license
// Modifications copyright � 2009 Edmund Kapusniak


using System;
using System.Collections.Generic;
using System.Reflection;
using Lua.Compiler.Frontend.Parser;
using Lua.Compiler.Frontend.AST;


namespace Lua.Compiler.Intermediate.IR.Expression
{



// <name>

sealed class GlobalExpression
	:	IRExpression
{

	public override bool	IsComplexAssignment	{ get { return true; } }
	public string			Name				{ get; private set; }


	public GlobalExpression( SourceLocation l, string name )
		:	base( l )
	{
		Name = name;
	}


	public override string ToString()
	{
		return Name;
	}




}




}

