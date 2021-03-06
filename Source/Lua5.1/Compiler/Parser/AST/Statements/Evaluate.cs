// Evaluate.cs
//
// Lua 5.1 is copyright � 1994-2008 Lua.org, PUC-Rio, released under the MIT license
// This file � 2009 Edmund Kapusniak


using System;
using Lua.Bytecode;


namespace Lua.Compiler.Parser.AST.Statements
{


class Evaluate
	:	Statement
{
	public Expression Expression { get; private set; }

	
	public Evaluate( SourceSpan s, Expression expression )
		:	base( s )
	{
		Expression = expression;
	}


	public override void Accept( IStatementVisitor v )
	{
		v.Visit( this );
	}

}

}
