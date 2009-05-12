// CallExpression.cs
//
// Lua 5.1 is copyright � 1994-2008 Lua.org, PUC-Rio, released under the MIT license
// LuaCLR is copyright � 2007-2008 Fabio Mascarenhas, released under the MIT license
// Modifications copyright � 2009 Edmund Kapusniak


using System;
using System.Collections.Generic;
using System.Reflection;
using Lua.Compiler.Front.Parser;
using Lua.Compiler.Front.AST;


namespace Lua.Compiler.Middle.IR
{



// <function>( <arguments> [, valuelist |, varargs ] )

sealed class CallExpression
	:	BaseCallExpression
{

	public IRExpression	Function	{ get; private set; }

	
	public CallExpression( SourceLocation l, IRExpression function, IList< IRExpression > arguments )
		:	base( l, arguments )
	{
		Function = function;
	}


	public override void Transform( IRCode code )
	{
		Function = Function.TransformExpression( code );
		base.Transform( code );
	}

}




}
