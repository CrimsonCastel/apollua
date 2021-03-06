// Call.cs
//
// Lua 5.1 is copyright � 1994-2008 Lua.org, PUC-Rio, released under the MIT license
// This file � 2009 Edmund Kapusniak


using System;
using System.Collections.Generic;
using Lua.Bytecode;


namespace Lua.Compiler.Parser.AST.Expressions
{


class Call
	:	Expression
{
	public Expression			Function		{ get; private set; }
	public IList< Expression >	Arguments		{ get; private set; }
	public Expression			ArgumentValues	{ get; private set; }


	public Call( SourceSpan s, Expression function, IList< Expression > arguments, Expression argumentValues )
		:	base( s )
	{
		Function		= function;
		Arguments		= arguments;
		ArgumentValues	= argumentValues;
	}


	public override void Accept( IExpressionVisitor v )
	{
		v.Visit( this );
	}

}



}

