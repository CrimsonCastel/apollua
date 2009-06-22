﻿// BytecodeTransform.cs
//
// Lua 5.1 is copyright © 1994-2008 Lua.org, PUC-Rio, released under the MIT license
// LuaCLR is copyright © 2007-2008 Fabio Mascarenhas, released under the MIT license
// This version copyright © 2009 Edmund Kapusniak


using System;
using System.Collections.Generic;
using Lua.Parser.AST;
using Lua.Parser.AST.Expressions;
using Lua.Parser.AST.Statements;


namespace Lua.VM.Compiler
{


/*	The Lua VM has some specialized bytecodes to implement language features.  However
	our very general AST has moved away from the specifics of the language.  So transform
	some constructions back to more specific statements so the compiler can be as
	simple as possible.

		->  For loops are transformed to use the specific bytecodes.
		->  SetList used to set multiple array-like keys at once in constructors
		->  Concatenation expressions merged to concatenate whole lists.
		->  Valuelist expressions transformed into TemporaryList.

*/

public class BytecodeTransform
	:	FunctionTransform
{
	TemporaryList valueList;



	// For loops are transformed to use specific opcodes.

	public override void Visit( Block s )
	{
		if ( s.Name == "fornum" )
		{
			// Transform to use forprep and for opcodes.
			result = ForNum( s );
		}
		else if ( s.Name == "forlist" )
		{
			// Transform to use tforloop opcode.
			result = ForList( s );
		}
		else
		{
			base.Visit( s );
		}
	}

	
	Block ForNum( Block b )
	{
		/*		block
					local (for index)	= tonumber( <index> )
					local (for limit)	= tonumber( <limit> )
					local (for step)	= tonumber( <step> )
			fornum:
					bfalse (    ( (for step) > 0 and (for index) <= (for limit) )
							 or ( (for step) < 0 and (for index) >= (for limit) ) ) fornumBreak
					block
						local <index> = (for index)
						...
					end
			fornumContinue:
					(for index) = (for index) + (for step)
					b fornum
			fornumBreak:
				end

		-->

				block
					local (for index)	= tonumber( <index> )
					local (for limit)	= tonumber( <limit> )
					local (for step)	= tonumber( <step> )
					forprep (for index), (for limit), (for step) fornumContinue
			fornum:
					block
						forindex <index>
						...
					end
			fornumContinue:
					forloop (for index), (for limit), (for step), <index> fornum
			fornumBreak:
				end
		*/


		// Extract existing values etc.
		// Relies on the exact layout of the existing fornum block.

		Variable	forIndex		= b.Locals[ 0 ];
		Variable	forLimit		= b.Locals[ 1 ];
		Variable	forStep			= b.Locals[ 2 ];
		LabelAST	fornum			= ( (MarkLabel)b.Statements[ 3 ] ).Label;
		LabelAST	fornumBreak		= ( (MarkLabel)b.Statements[ 9 ] ).Label;
		LabelAST	fornumContinue	= ( (MarkLabel)b.Statements[ 6 ] ).Label;
		Block		fornumBody		= (Block)b.Statements[ 5 ];
		Variable	userIndex		= ( (Declare)fornumBody.Statements[ 0 ] ).Variable;
		

		// Construct new fornum block.

		SourceSpan s = b.Statements[ 0 ].SourceSpan;

		block = new Block( b.SourceSpan, block, "fornum" );

		block.Statement( b.Statements[ 0 ] );
		block.Statement( b.Statements[ 1 ] );
		block.Statement( b.Statements[ 2 ] );

		block.Statement( new OpcodeForPrep( s, forIndex, forLimit, forStep, fornumContinue ) );
		block.Statement( new MarkLabel( s, fornum ) );


		// Transform loop body.

		block = new Block( fornumBody.SourceSpan, block, "fornumBody" );
		block.Parent.Statement( block );

		block.Statement( new DeclareForIndex( s, userIndex ) );

		foreach ( Variable local in fornumBody.Locals )
		{
			block.Local( local );
		}

		// Miss out the first statement, which used to declare the user index.
		// Break and continue branches should branch to the correct labels we've extracted.

		for ( int i = 1; i < fornumBody.Statements.Count ; ++i )
		{
			Statement transformed = Transform( fornumBody.Statements[ i ] );
			if ( transformed != null )
			{
				block.Statement( transformed );
			}
		}

		block = block.Parent;


		// Loop epilog.

		s = b.Statements[ 6 ].SourceSpan;

		block.Statement( new MarkLabel( s, fornumContinue ) );
		block.Statement( new OpcodeForLoop( s, forIndex, forLimit, forStep, userIndex, fornum ) );
		block.Statement( new MarkLabel( s, fornumBreak ) );


		// Return.

		b = block;
		block = block.Parent;
		return b;
	}


	Block ForList( Block b )
	{
		/*		block
					local (for generator), (for state), (for control) = <expressionlist>
			forlistContinue:
					block
						local <variablelist> = (for generator) ( (for state), (for control) )
						(for control) = <variablelist>[ 0 ]
						btrue ( (for control) == nil ) forlistBreak
						...
					end
					b forlistContinue
			forlistBreak:
				end
				
		-->
				
				block
					local (for generator), (for state), (for control) = <expressionlist>
					b forlistContinue
			forlist:
					block
						forindex <variablelist>
						...
					end
			forlistContinue:
					tforloop (for generator), (for state), (for control), <variablelist> forlist
			forlistBreak:
				end
		*/


		// Extract existing values etc.
		// Relies on the exact layout of the existing forlist block.

		int prolog = 0; while ( !( b.Statements[ prolog ] is MarkLabel ) ) prolog += 1;

		Variable	forGenerator	= b.Locals[ 0 ];
		Variable	forState		= b.Locals[ 1 ];
		Variable	forControl		= b.Locals[ 2 ];
		LabelAST	forlist			= new LabelAST( "forlist" ); function.Label( forlist );
		LabelAST	forlistBreak	= ( (MarkLabel)b.Statements[ prolog + 3 ] ).Label;
		LabelAST	forlistContinue	= ( (MarkLabel)b.Statements[ prolog + 0 ] ).Label;
		Block		forlistBody		= (Block)b.Statements[ prolog + 1 ];
		
		int body = 0; while ( !( forlistBody.Statements[ body ] is Declare ) ) body += 1;

		List< Variable > variables	= new List< Variable >();
		while ( forlistBody.Statements[ body ] is Declare )
		{
			variables.Add( ( (Declare)forlistBody.Statements[ body ] ).Variable );
			body += 1;
		}

		body += 2; // skip (for control) update and end loop test
		

		// Construct new forlist block.

		block = new Block( b.SourceSpan, block, "forlist" );
		for ( int i = 0; i < prolog; ++i )
		{
			Statement transformed = Transform( b.Statements[ i ] );
			if ( transformed != null )
			{
				block.Statement( transformed );
			}
		}

		SourceSpan s = b.Statements[ prolog ].SourceSpan;

		block.Statement( new Branch( s, forlistContinue ) );
		block.Statement( new MarkLabel( s, forlist ) );


		// Transform loop body.

		block = new Block( forlistBody.SourceSpan, block, "forlistBody" );
		block.Parent.Statement( block );

		foreach ( Variable local in forlistBody.Locals )
		{
			block.Local( local );
		}

		// Miss out the start of the body, which is covered by tforloop now.
		// Break and continue branches should branch to the correct labels we've extracted.

		foreach ( Variable variable in variables )
		{
			block.Statement( new DeclareForIndex( s, variable ) );
		}
		
		for ( int i = body; i < forlistBody.Statements.Count ; ++i )
		{
			Statement transformed = Transform( forlistBody.Statements[ i ] );
			if ( transformed != null )
			{
				block.Statement( transformed );
			}
		}

		block = block.Parent;


		// Loop epilog.

		s = b.Statements[ prolog + 3 ].SourceSpan;

		block.Statement( new MarkLabel( s, forlistContinue ) );
		block.Statement( new OpcodeTForLoop( s,
				forGenerator, forState, forControl, variables.AsReadOnly(), forlist ) );
		block.Statement( new MarkLabel( s, forlistBreak ) );


		// Return.

		b = block;
		block = block.Parent;
		return b;
	}



	// Constructors are transformed to use setlist for array-like elements.

	public override void Visit( Constructor s )
	{
		Constructor constructor = new Constructor( s.SourceSpan, s.Temporary, s.ArrayCount, s.HashCount );
		

		// Check if the constructor ends with a multiple values expression.

		Expression values = null;
		int assignCount = s.Statements.Count;
		if ( assignCount > 0 )
		{
			IndexMultipleValues imv = s.Statements[ assignCount - 1 ] as IndexMultipleValues;
			if ( imv != null )
			{
				values = imv.Values;
				assignCount -= 1;
			}
		}


		// Batch up array-type assignments.

		int key		= 1;
		int nextkey	= 1;
		List< Expression > operands = new List< Expression >();

		for ( int i = 0; i < assignCount; ++i )
		{
			Statement	statement	= s.Statements[ i ];
			Assign		assign		= (Assign)statement;
			Index		target		= (Index)assign.Target;
			Expression	value		= assign.Value;
			Literal		literal		= target.Key as Literal;

			if ( ( literal != null ) && literal.Value.Equals( nextkey ) )
			{
				// Assign to temporaries so we don't mess up the order of execution, this
				// will be translated by the compiler to subsequent stack locations.

				Temporary temporary = new Temporary( statement.SourceSpan );
				constructor.Statement( new Assign( statement.SourceSpan, temporary, value ) );
				operands.Add( temporary );
				nextkey += 1;

				if ( operands.Count == Instruction.FieldsPerFlush )
				{
					// Set all accumulated operands and reset.

					constructor.Statement( new OpcodeSetList(
						s.SourceSpan, s.Temporary, key, operands.AsReadOnly(), null ) );
					
					key += operands.Count;
					operands = new List< Expression >();
				}

			}
			else
			{
				constructor.Statement( Transform( s.Statements[ i ] ) );
			}
		}


		// Set remaining batched values as well as multiple values, if necessary.

		if ( ( values != null ) || ( operands.Count > 0 ) )
		{
			constructor.Statement( new OpcodeSetList(
				s.SourceSpan, s.Temporary, key, operands.AsReadOnly(), values ) );
		}


		// Return.

		result = constructor;
	}




	// String concatenation operations are merged to concatenate full lists.

	public override void Visit( Binary e )
	{
		if ( e.Op == BinaryOp.Concatenate )
		{
			List< Expression > operands = new List< Expression >();
			ConcatenateList( operands, e );
			SourceSpan s = new SourceSpan( operands[ 0 ].SourceSpan.Start,
									operands[ operands.Count - 1 ].SourceSpan.End );
			result = new OpcodeConcat( s, operands.AsReadOnly() );
		}
		else
		{
			base.Visit( e );
		}
	}


	void ConcatenateList( List< Expression > operands, Expression e )
	{
		Binary binary = e as Binary;
		if ( ( binary != null ) && ( binary.Op == BinaryOp.Concatenate ) )
		{
			ConcatenateList( operands, binary.Left );
			ConcatenateList( operands, binary.Right );
		}
		else
		{
			operands.Add( Transform( e ) );
		}
	}



	// ValueLists become TemporaryLists so the compiler has less to worry about.

	public override void Visit( ValueList e )
	{
		// Construct list of temporaries.

		Temporary[] temporaries = new Temporary[ e.ElementCount ];
		for ( int element = 0; element < e.ElementCount; ++element )
		{
			temporaries[ element ] = new Temporary( e.SourceSpan );
		}


		// Replace valuelist with it.

		valueList = new TemporaryList( e.SourceSpan, Array.AsReadOnly( temporaries ) );
		result = valueList;
	}

	public override void Visit( ValueListElement e )
	{
		result = valueList.Temporaries[ e.Index ];
	}
	


}


}
