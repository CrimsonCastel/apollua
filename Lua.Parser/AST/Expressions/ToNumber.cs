// ToNumber.cs
//
// Lua 5.1 is copyright � 1994-2008 Lua.org, PUC-Rio, released under the MIT license
// LuaCLR is copyright � 2007-2008 Fabio Mascarenhas, released under the MIT license
// Modifications copyright � 2009 Edmund Kapusniak


using System;


namespace Lua.Parser.AST.Expressions
{


public class ToNumber
	:	Expression
{
	public Expression Operand { get; private set; }


	public ToNumber( SourceSpan s, Expression operand )
		:	base( s )
	{
		Operand = operand;
	}


	public override void Accept( ExpressionVisitor v )
	{
		v.Visit( this );
	}

}


}