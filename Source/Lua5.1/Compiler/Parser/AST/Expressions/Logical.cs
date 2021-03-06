// Logical.cs
//
// Lua 5.1 is copyright � 1994-2008 Lua.org, PUC-Rio, released under the MIT license
// This file � 2009 Edmund Kapusniak


using System;
using Lua.Bytecode;


namespace Lua.Compiler.Parser.AST.Expressions
{


class Logical
	:	Expression
{
	public LogicalOp	Op			{ get; private set; }
	public Expression	Left		{ get; private set; }
	public Expression	Right		{ get; private set; }


	public Logical( SourceSpan s, LogicalOp op, Expression left, Expression right )
		:	base( s )
	{
		Op		= op;
		Left	= left;
		Right	= right;
	}


	public override void Accept( IExpressionVisitor v )
	{
		v.Visit( this );
	}

}


}