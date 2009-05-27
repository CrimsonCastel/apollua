// Parser.cs
//
// Lua 5.1 is copyright � 1994-2008 Lua.org, PUC-Rio, released under the MIT license
// LuaCLR is copyright � 2007-2008 Fabio Mascarenhas, released under the MIT license
// Modifications copyright � 2009 Edmund Kapusniak


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Lua.Parser.AST;
using Lua.Parser.AST.Expressions;
using Lua.Parser.AST.Statements;


namespace Lua.Parser
{


public class Parser
{
	// Errors.

	TextWriter	errorWriter;
	bool		hasError;


	// Lexer.
	
	string		sourceName;
	Lexer		lexer;
	Token		lookahead;
	Token		token;


	// Parse state.

	Function					function;
	Scope						scope;
	ParseStack< Expression >	expression;
	ParseStack< string >		name;



	// Public.

	public Parser( TextWriter errorWriter, TextReader sourceReader, string sourceName )
	{
		this.errorWriter	= errorWriter;
		hasError			= false;

		this.sourceName		= sourceName;
		lexer				= new Lexer( errorWriter, sourceReader, sourceName );
		Next();

		function			= null;
		scope				= null;
		expression			= new ParseStack< Expression >();
		name				= new ParseStack< string >();
	}


	public bool HasError
	{
		get { return hasError; }
	}


	public Function Parse()
	{
		return chunk();
	}




	// Chunk

	Function chunk()
	{
		/*	goal chunk
				: block eof
				;
		*/

		Debug.Assert( function == null );
		Debug.Assert( scope == null );

		function = new Function( "<chunk>", function );
		scope = new Scope( function, null );
	
		function.SetVararg();

		block();

		Token eof = Check( TokenKind.EOF );

		Function result = function;
		function = function.Parent;
		scope = scope.Parent;

		Debug.Assert( function == null );
		Debug.Assert( scope == null );
		Debug.Assert( expression.Count == 0 );
		Debug.Assert( name.Count == 0 );
		
		return result;
	}



	// Blocks
	

	bool block_follow_set()
	{
		switch ( Get() )
		{
		case TokenKind.Else:
		case TokenKind.Elseif:
		case TokenKind.End:
		case TokenKind.Until:
		case TokenKind.EOF:
			return true;

		default:
			return false;
		}
	}

	void block()
	{
		/*  block
				: ( stat ';'? )*  ( laststat ';'? )?
				;
		*/

		bool isLastStatement = false;
		while ( ! isLastStatement && ! block_follow_set() )
		{
			isLastStatement = statement();
			Test( TokenKind.Semicolon );
		}
	}



	// Statements


	bool statement()
	{
		/*	statement
				: stat		{ returns false }
				| laststat	{ returns true }
				;

			stat
				: exprstat
				| dostat
				| whilestat
				| repeatstat
				| ifstat
				| forstat
				| funcstat
				| localfunc
				| localstat
				;

			laststat
				: retstat
				| breakstat
				| continuestat
				;
		*/

		switch ( Get() )
		{
		case TokenKind.Do:
			dostat();
			return false;

		case TokenKind.While:
			whilestat();
			return false;

		case TokenKind.Repeat:
			repeatstat();
			return false;

		case TokenKind.If:
			ifstat();
			return false;

		case TokenKind.For:
			forstat();
			return false;

		case TokenKind.Function:
			funcstat();
			return false;

		case TokenKind.Local:
			if ( Lookahead() == TokenKind.Function )
			{
				localfunc();
			}
			else
			{
				localstat();
			}
			return false;

		case TokenKind.Return:
			retstat();
			return true;

		case TokenKind.Break:
			breakstat();
			return true;

		case TokenKind.Continue:
			continuestat();
			return true;

		default:
			exprstat();
			return false;
		}
	}


	void dostat()
	{
		/*	dostat
				: 'do' block 'end'
				;
		*/
	
		/*	scope
			{
		...
			}
		*/

		Token matchDo = Check( TokenKind.Do );
		function.Statement( new BeginScope( matchDo.SourceSpan ) );
		scope = new Scope( function, scope );
		
		block();
	
		Token endDo = Check( TokenKind.End, matchDo );
		function.Statement( new EndScope( endDo.SourceSpan ) );
		scope = scope.Parent;
	}


	void whilestat()
	{
		/*	whilestat
				: 'while' exp 'do' block 'end'
				;
		*/

		/*	block while
			{
				scope
				{
					test <condition>
					{
		...
						continue while
					}
				}
			}
		*/

		Token matchWhile = Check( TokenKind.While );
		exp();
		Expression condition = PopValue();
		Token doToken = Check( TokenKind.Do );

		SourceSpan s = new SourceSpan( matchWhile.SourceSpan.Start, doToken.SourceSpan.End );

		function.Statement( new BeginBlock( s, "while" ) );
		function.Statement( new BeginScope( s ) );
		function.Statement( new BeginTest( s, condition ) );
		scope = new Scope( function, scope,
			new BreakContinueAction( BreakOrContinue.Break, "while" ),
			new BreakContinueAction( BreakOrContinue.Continue, "while" ) );
				
		block();

		Token endWhile = Check( TokenKind.End, matchWhile );
		
		s = endWhile.SourceSpan;
		
		function.Statement( new Continue( s, "while" ) );
		function.Statement( new EndTest( s ) );
		function.Statement( new EndScope( s ) );
		function.Statement( new EndBlock( s ) );
		scope = scope.Parent;
	}


	void repeatstat()
	{
		/*	repeatstat
				: 'repeat' block 'until' exp
				;
		*/

		/*	block repeat
			{
				scope
				{
					block repeatbody
					{
		...
					}
					test <condition>
					{
						continue repeat
					}
				}
			}
		*/

		Token matchRepeat = Check( TokenKind.Repeat );

		SourceSpan s = matchRepeat.SourceSpan;

		function.Statement( new BeginBlock( s, "repeat" ) );
		function.Statement( new BeginScope( s ) );
		function.Statement( new BeginBlock( s, "repeatbody" ) );
		scope = new Scope( function, scope, 
			new BreakContinueAction( BreakOrContinue.Break, "repeat" ),
			new BreakContinueAction( BreakOrContinue.Break, "repeatbody" ) );

		block();
		Token until = Check( TokenKind.Until, matchRepeat );
		exp();
		Expression condition = PopValue();

		s = new SourceSpan( until.SourceSpan.Start, condition.SourceSpan.End );

		function.Statement( new EndBlock( s ) );
		function.Statement( new BeginTest( s, condition ) );
		function.Statement( new Continue( s, "repeat" ) );
		function.Statement( new EndTest( s ) );
		function.Statement( new EndScope( s ) );
		function.Statement( new EndBlock( s ) );
		scope = scope.Parent;
	}


	void ifstat()
	{
		/*	ifstat
				: 'if' exp 'then' block ( 'elseif' exp 'then' block )* ( 'else' block )? 'end'
				;
		*/

		/*	block if
			{
				scope
				{
					test <condition>
					{
		...	
						break if
					}
				}
				scope
				{
					test <condition>
					{
		...
						break if
					}
				}
				scope
				{
			...
				}
			}
		*/

		Token match = Check( TokenKind.If );
		exp();
		Expression condition = PopValue();
		Token thenToken = Check( TokenKind.Then );

		SourceSpan s = new SourceSpan( match.SourceSpan.Start, thenToken.SourceSpan.End );

		function.Statement( new BeginBlock( s, "if" ) );
		function.Statement( new BeginScope( s ) );
		function.Statement( new BeginTest( s, condition ) );
		scope = new Scope( function, scope );

		block();

		while ( Get() == TokenKind.Elseif )
		{
			Token elseIf = Check( TokenKind.Elseif );

			s = elseIf.SourceSpan;

			function.Statement( new Break( s, "if" ) );
			function.Statement( new EndTest( s ) );
			function.Statement( new EndScope( s ) );
			scope = scope.Parent;
			
			exp();
			condition = PopValue();
			thenToken = Check( TokenKind.Then );

			s = new SourceSpan( elseIf.SourceSpan.Start, thenToken.SourceSpan.End );
	
			function.Statement( new BeginScope( s ) );
			function.Statement( new BeginTest( s, condition ) );
			scope = new Scope( function, scope );
			
			block();
		}

		if ( Get() == TokenKind.Else )
		{
			Token elseToken = Check( TokenKind.Else );

			s = elseToken.SourceSpan;

			function.Statement( new Break( s, "if" ) );
			function.Statement( new EndTest( s ) );
			function.Statement( new EndScope( s ) );
			scope = scope.Parent;

			function.Statement( new BeginScope( s ) );
			scope = new Scope( function, scope );

			block();

			Token endIf = Check( TokenKind.End, match );

			s = endIf.SourceSpan;
		}
		else
		{
			Token endIf = Check( TokenKind.End, match );

			s = endIf.SourceSpan;

			function.Statement( new EndTest( s ) );
		}


		function.Statement( new EndScope( s ) );
		function.Statement( new EndBlock( s ) );
		scope = scope.Parent;
	}


	void forstat()
	{
		/*	forstat
				: 'for' fornum 'end'
				| 'for' forlist 'end'
				;
		*/

		Token matchFor = Check( TokenKind.For );
		
		if ( Get() != TokenKind.Identifier )
		{
			Error( "{0} expected", Lexer.GetTokenName( TokenKind.Identifier ) );
			return;
		}

		switch ( Lookahead() )
		{
		case TokenKind.EqualSign:
			fornum( matchFor );
			break;

		case TokenKind.Comma:
		case TokenKind.In:
			forlist( matchFor );
			break;

		default:
			Error( "{0}, {1} or {2} expected", Lexer.GetTokenName( TokenKind.In ),
				Lexer.GetTokenName( TokenKind.Comma ), Lexer.GetTokenName( TokenKind.EqualSign ) );
			return;
		}
		
	}


	void fornum( Token matchFor )
	{
		/*	fornum
				: IDENTIFIER '=' exp ',' exp ( ',' exp )? 'do' block
				;
		*/

		/*	scope
			{
				local (for index)	= tonumber( <index> )
				local (for limit)	= tonumber( <limit> )
				local (for step)	= tonumber( <step> )
				block for
				{
					scope
					{
						test (    ( (for step) > 0 and (for index) <= (for limit) )
							   or ( (for step) < 0 and (for index) >= (for limit) ) )
						{
							block forbody
							{
								local <index> = (for index)
		...
							}
							(for index) = (for index) + (for step)
							continue for
						}
					}
				}
			}
		*/

		Token name = Check( TokenKind.Identifier );
		Check( TokenKind.EqualSign );
		exp();
		Expression start = expression.Pop();
		Check( TokenKind.Comma );
		exp();
		Expression limit = expression.Pop();

		Expression step = null;
		if ( Test( TokenKind.Comma ) )
		{
			exp();
			step = expression.Pop();
		}
		else
		{
			step = new Literal( matchFor.SourceSpan, (int)1 );
		}

		Token doToken = Check( TokenKind.Do );

		
		// Construct AST.

		SourceSpan s = new SourceSpan( matchFor.SourceSpan.Start, doToken.SourceSpan.End );

		function.Statement( new BeginScope( s ) );
		

		// Delcare internal variables.

		Variable forIndex = new Variable( "(for index)" );	function.Local( forIndex );
		Variable forLimit = new Variable( "(for limit)" );	function.Local( forLimit );
		Variable forStep  = new Variable( "(for step)" );	function.Local( forStep );

		Expression startExpression = new ToNumber( start.SourceSpan, start );
		Expression limitExpression = new ToNumber( limit.SourceSpan, limit );
		Expression stepExpression  = new ToNumber( step.SourceSpan, step );

		function.Statement( new DeclareAssign( start.SourceSpan, forIndex, startExpression ) );
		function.Statement( new DeclareAssign( limit.SourceSpan, forLimit, limitExpression ) );
		function.Statement( new DeclareAssign( step.SourceSpan, forStep, stepExpression ) );


		// Test expression.

		Expression test =
			new Logical( s, LogicalOp.Or,
				new Logical( s, LogicalOp.And, 
					new Comparison( s, ComparisonOp.GreaterThan,
						new Local( s, forStep ),
						new Literal( s, 0.0 ) ),
					new Comparison( s, ComparisonOp.LessThanOrEqual,
						new Local( s, forIndex ),
						new Local( s, forLimit ) ) ),
				new Logical( s, LogicalOp.And,
					new Comparison( s, ComparisonOp.LessThan,
						new Local( s, forStep ),
						new Literal( s, 0.0 ) ),
					new Comparison( s, ComparisonOp.GreaterThanOrEqual,
						new Local( s, forIndex ),
						new Local( s, forLimit ) ) ) );


		// Loop body.

		function.Statement( new BeginBlock( s, "for" ) );
		function.Statement( new BeginScope( s ) );
		function.Statement( new BeginTest( s, test ) );
		function.Statement( new BeginBlock( s, "forbody" ) );
		

		// Declare index variable.

		scope = new Scope( function, scope,
			new BreakContinueAction( BreakOrContinue.Break, "for" ),
			new BreakContinueAction( BreakOrContinue.Break, "forbody" ) );
		Variable userIndex = new Variable( (string)name.Value );
		function.Local( userIndex ); scope.Local( userIndex );
		Expression indexExpression = new Local( s, forIndex );
		function.Statement( new DeclareAssign( s, userIndex, indexExpression ) );


		// Loop body.

		block();

		Token endFor = Check( TokenKind.End, matchFor );


		// Close AST.

		s = endFor.SourceSpan;

		function.Statement( new EndBlock( s ) );
	
		
		// Increment index.

		Expression indexVariable = new Local( s, forIndex );
		Expression incrementExpression  =
			new Binary( s, BinaryOp.Add,
				new Local( s, forIndex ),
				new Local( s, forStep ) );

		function.Statement( new Assign( s, indexVariable, incrementExpression ) );



		// Finish loop.

		function.Statement( new Continue( s, "for" ) );
		function.Statement( new EndTest( s ) );
		function.Statement( new EndScope( s ) );
		function.Statement( new EndBlock( s ) );	
		function.Statement( new EndScope( s ) );
	}


	void forlist( Token matchFor )
	{
		/*	forlist
				: IDENTIFIER ( ',' IDENTIFIER )* 'in' explist 'do' block
				;
		*/

		
		name.Push( (string)Check( TokenKind.Identifier ).Value );
		int namecount = 1;
		
		while ( Test( TokenKind.Comma ) )
		{
			name.Push( (string)Check( TokenKind.Identifier ).Value );
			namecount += 1;
		}

		Check( TokenKind.In );
		int expressioncount = explist();

		Token doToken = Check( TokenKind.Do );


		// Build AST

		SourceSpan s = new SourceSpan( matchFor.SourceSpan.Start, doToken.SourceSpan.End );


		// Declare internal variables.

		scope = new Scope( function, scope );
		name.Push( "(for generator)" );
		name.Push( "(for state)" );
		name.Push( "(for control)" );
		//LocalStatementAST( s, 3, expressioncount );

		Debug.Assert( scope.Locals.Count == 3 );
		Variable forGenerator	= scope.Locals[ 0 ];
		Variable forState		= scope.Locals[ 1 ];
		Variable forControl		= scope.Locals[ 2 ];
		Debug.Assert( forGenerator.Name	== "(for generator)" );
		Debug.Assert( forState.Name		== "(for state)" );
		Debug.Assert( forControl.Name	== "(for control)" );

		scope = scope.Parent;


		// For loop block.

		function.Statement( new BeginBlock( s, "forin" ) );
		function.Statement( new BeginScope( s ) );


		// Generator expressin.

		Expression generator =
			new Call( s,
				new Local( s, forGenerator ),
				new Expression[] {
					new Local( s, forState ),
					new Local( s, forControl ) },
				null );


		// Declare user variables.

		scope = new Scope( function, scope,
			new BreakContinueAction( BreakOrContinue.Break, "forin" ),
			new BreakContinueAction( BreakOrContinue.Continue, "forin" ) );
		expression.Push( generator );
		//LocalStatementAST( s, namecount, 1 );

		Debug.Assert( scope.Locals.Count == namecount );
		Variable userControl = scope.Locals[ 0 ];


		// Test expression.

		Expression test =
			new Comparison( s, ComparisonOp.Equal,
				new Local( s, forControl ),
				new Literal( s, null ) );


		// Update control and test.

		Expression controlVariable	= new Local( s, forControl );
		Expression updateExpression	= new Local( s, userControl );

		function.Statement( new Assign( s, controlVariable, updateExpression ) );
		function.Statement( new BeginTest( s, test ) );
		function.Statement( new Break( s, "forin" ) );
		function.Statement( new EndTest( s ) );


		// Loop body.

		block();

		Token endFor = Check( TokenKind.End, matchFor );


		// Close AST.

		s = endFor.SourceSpan;

		function.Statement( new Continue( s, "forin" ) );
		function.Statement( new EndScope( s ) );
		function.Statement( new EndBlock( s ) );
		function.Statement( new EndScope( s ) );
	}


	void funcstat()
	{
		/*	funcstat
				: 'function' funcname funcbody
				;
		
			funcname
				: IDENTIFIER ( '.' IDENTIFIER )* ( ':' IDENTIFIER )
				;
		*/

		Token matchFunction = Check( TokenKind.Function );

		Token variableName = Check( TokenKind.Identifier );
		Expression variable = Lookup( variableName );
		StringBuilder functionName = new StringBuilder( (string)variableName.Value );

		while ( Get() == TokenKind.FullStop )
		{
			Token fullStop = Check( TokenKind.FullStop );
			Token key = Check( TokenKind.Identifier );
			
			variable = new Index(
				new SourceSpan( variable.SourceSpan.Start, key.SourceSpan.End ),
				variable,
				new Literal( key.SourceSpan, (string)key.Value ) );

			functionName.Append( "." );
			functionName.Append( (string)key.Value );
		}

		Token? methodName = null;
		if ( Get() == TokenKind.Colon )
		{
			Token colon = Check( TokenKind.Colon );
			Token methodNameToken = Check( TokenKind.Identifier );

			variable = new Index(
				new SourceSpan( variable.SourceSpan.Start, methodNameToken.SourceSpan.End ),
				variable,
				new Literal( methodNameToken.SourceSpan, (string)methodNameToken.Value ) );

			functionName.Append( ":" );
			functionName.Append( (string)methodNameToken.Value );

			methodName = methodNameToken;
		}

		funcbody( matchFunction, functionName.ToString(), methodName );
		Expression f = expression.Pop();
	
		function.Statement( new Assign( variable.SourceSpan, variable, f ) );
	}


	void localfunc()
	{
		/*	localfunc
				: 'local' 'function' IDENTIFIER funcbody
				;
		*/

		Check( TokenKind.Local );
		Token matchFunction = Check( TokenKind.Function );
		Token localName = Check( TokenKind.Identifier );

		funcbody( matchFunction, (string)localName.Value, null );
		Expression f = expression.Pop();

		Variable local = new Variable( (string)localName.Value );
		function.Local( local ); scope.Local( local );
		function.Statement( new DeclareAssign( localName.SourceSpan, local, f ) );
	}


	void funcbody( Token matchFunction, string functionName, Token? methodName )
	{
		/*	funcbody
				: '(' parlist ')' block 'end'
				;
		
			parlist
				: ( param ( ',' param )* )?
				;

			param
				: IDENTIFIER
				| '...'
				;
		*/

		// Function.

		function = new Function( functionName, function );
		function.Parent.ChildFunction( function );
		scope = new Scope( function, scope );



		// Parameters.

		if ( Get() == TokenKind.LeftParenthesis || Get() == TokenKind.NewlineLeftParenthesis )
		{
			Next();
		}
		else
		{
			Error( "{0} expected", Lexer.GetTokenName( TokenKind.LeftParenthesis ) );
		}

		if ( methodName.HasValue )
		{
			Variable self = new Variable( "self" );
			function.Parameter( self ); scope.Local( self );
		}

		if ( Get() != TokenKind.RightParenthesis )
		{
			while ( true )
			{
				// param

				if ( Get() == TokenKind.Identifier )
				{
					Token parameterToken = Check( TokenKind.Identifier );
					Variable parameter = new Variable( (string)parameterToken.Value );
					function.Parameter( parameter ); scope.Local( parameter );
				}
				else if ( Get() == TokenKind.Ellipsis )
				{
					Check( TokenKind.Ellipsis );
					function.SetVararg();
					break;
				}
				else
				{
					Error( "{0} or {1} expected", Lexer.GetTokenName( TokenKind.Identifier ),
						Lexer.GetTokenName( TokenKind.Ellipsis ) );
					break;
				}

				// ','

				if ( ! Test( TokenKind.Comma ) )
				{
					break;
				}
			}
		}

		Check( TokenKind.RightParenthesis );


		// Statements.

		block();

		Token endFunction = Check( TokenKind.End, matchFunction );


		// End function.

		Statement lastStatement = null;
		if ( function.Statements.Count > 0 )
		{
			lastStatement = function.Statements[ function.Statements.Count - 1 ];
		}

		if (    !( lastStatement is Return )
			 && !( lastStatement is ReturnMultipleResults ) )
		{
			function.Statement( new Return( endFunction.SourceSpan,
				new Literal( endFunction.SourceSpan, null ) ) );
		}


		// Push a function expression.

		SourceSpan s = new SourceSpan( matchFunction.SourceSpan.Start, endFunction.SourceSpan.End );
		expression.Push( new Closure( s, function ) );


		// Finished.

		scope = scope.Parent;
		function = function.Parent;
	}


	void localstat()
	{
		/*	localstat
				: 'local' IDENTIFIER ( ',' IDENTIFIER )* ( '=' explist )?
				;
		*/

		Token local = Check( TokenKind.Local );


		// Have to declare new variables after evaluating the explist, so
		// that local x = x will find the value of x in the enclosing scope.

		int variablecount = 0;
		while ( true )
		{
			// IDENTIFIER

			name.Push( (string)Check( TokenKind.Identifier ).Value );
			variablecount += 1;
			

			// ','

			if ( ! Test( TokenKind.Comma ) )
			{
				break;
			}
		}


		// explist

		int expressioncount = 0;
		if ( Get() == TokenKind.EqualSign )
		{
			Check( TokenKind.EqualSign );
			expressioncount = explist();
		}


		// Perform assignment.

		IList< Expression >	expressionlist	= expression.Pop( expressioncount );
		IList< string >		namelist		= name.Pop( variablecount );
		
		actions.Local( local.Location, scope.Peek(), namelist, expressionlist );
	}



	enum ExpressionType
	{
		None,
		FunctionCall,
		Assignable,
	}



	void exprstat()
	{
		/*	exprstat
				: primaryexp { not a function call } ( ',' primaryexp )* '=' explist
				| primaryexp { function call }
				;
		*/

		
		ExpressionType expressionType = primaryexp();
		
		if ( expressionType == ExpressionType.FunctionCall )
		{
			actions.CallStatement( callToken.Location, scope.Peek(), expression.Pop() );
			return;
		}
		
		
		bool assignmentError = false;
		if ( expressionType != ExpressionType.Assignable )
		{
			Error( "Expression is not assignable" );
			assignmentError = true;
		}


		int variablecount	= 1;
		while ( Test( TokenKind.Comma ) )
		{
			variablecount += 1;
			expressionType = primaryexp();
			if ( expressionType != ExpressionType.Assignable )
			{
				Error( "Expression is not assignable" );
				assignmentError = true;
			}
		}

		
		Token equalSign = Check( TokenKind.EqualSign );
		int expressioncount = explist();

		IList< Expression > expressionlist	= expression.Pop( expressioncount );
		IList< Expression > variablelist	= expression.Pop( variablecount );

		if ( ! assignmentError )
		{
			actions.Assignment( equalSign.Location, scope.Peek(), variablelist, expressionlist );
		}
	}



	void retstat()
	{
		/*	retstat
				: 'return' ( explist )?
				;
		*/

		Token returnToken = Check( TokenKind.Return );

		int expressioncount = 0;
		if ( ! block_follow_set() && Get() != TokenKind.Semicolon )
		{
			expressioncount = explist();
		}

		for ( int scopecount = 0; scopecount < scope.Count; ++scopecount )
		{
			Scope functionScope = scope.Peek( scopecount );
			if ( functionScope.IsFunctionScope )
			{
				actions.Return( returnToken.Location, functionScope, expression.Pop( expressioncount ) );
				return;
			}
		}

		expression.Pop( expressioncount );
		Error( "No function to return from" );
	}


	void breakstat()
	{
		/*	breakstat
				: 'break'
				;
		*/

		Token breakToken = Check( TokenKind.Break );

		for ( int scopecount = 0; scopecount < scope.Count; ++scopecount )
		{
			Scope loopScope = scope.Peek( scopecount );
			if ( loopScope.IsLoopScope )
			{
				actions.Break( breakToken.Location, loopScope );
				return;
			}
		}

		Error( "No loop to break" );
	}


	void continuestat()
	{
		/*	continuestat
				: 'continue'
				;
		*/

		Token continueToken = Check( TokenKind.Continue );

		for ( int scopecount = 0; scopecount < scope.Count; ++scopecount )
		{
			Scope loopScope = scope.Peek( scopecount );
			if ( loopScope.IsLoopScope )
			{
				actions.Continue( continueToken.Location, loopScope );
				return;
			}
		}

		Error( "No loop to continue" );
	}




	// Expressions


	struct Operator
	{
		public int	LeftPriority	{ get; private set; }
		public int	RightPriority	{ get; private set; }


		public Operator( int leftPriority, int rightPriority )
			:	this()
		{
			LeftPriority	= leftPriority;
			RightPriority	= rightPriority;
		}
	}

	static readonly Dictionary< TokenKind, Operator > unaryOperators = new Dictionary< TokenKind, Operator >
	{
		{ TokenKind.Not,				new Operator( 8, 8 ) },
		{ TokenKind.HyphenMinus,		new Operator( 8, 8 ) },
		{ TokenKind.NumberSign,			new Operator( 8, 8 ) },
	};
	
	static readonly Dictionary< TokenKind, Operator > binaryOperators = new Dictionary< TokenKind, Operator >
	{
		{ TokenKind.CircumflexAccent,	new Operator( 10, 9 ) },

		{ TokenKind.Solidus,			new Operator( 7, 7 ) },
		{ TokenKind.PercentSign,		new Operator( 7, 7 ) },
		{ TokenKind.Asterisk,			new Operator( 7, 7 ) },

		{ TokenKind.PlusSign,			new Operator( 6, 6 ) },
		{ TokenKind.HyphenMinus,		new Operator( 6, 6 ) },

		{ TokenKind.Concatenate,		new Operator( 5, 4 ) },

		{ TokenKind.LogicalEqual,		new Operator( 3, 3 ) },
		{ TokenKind.NotEqual,			new Operator( 3, 3 ) },
		{ TokenKind.GreaterThanSign,	new Operator( 3, 3 ) },
		{ TokenKind.GreaterThanOrEqual,	new Operator( 3, 3 ) },
		{ TokenKind.LessThanSign,		new Operator( 3, 3 ) },
		{ TokenKind.LessThanOrEqual,	new Operator( 3, 3 ) },

		{ TokenKind.And,				new Operator( 2, 2 ) },
		{ TokenKind.Or,					new Operator( 1, 1 ) },
	};


	int explist()
	{
		/*	explist
				: exp ( ',' exp )*
				;
		*/

		int expressioncount = 0;
		while ( true )
		{
			// exp

			exp();
			expressioncount += 1;
			

			// ','

			if ( ! Test( TokenKind.Comma ) )
			{
				break;
			}
		}


		return expressioncount;
	}


	void exp()
	{
		subexpr( 0 );
	}

	
	void subexpr( int limit )
	{
		/*	subexpr	{ shift-reduce conflict on binop is resolved using operator precedence }
				:	( simplexp | unop subexpr ) ( binop subexpr )*
				;
		*/

		
		// Check for unary operator.

		Operator unaryOperator;
		if ( unaryOperators.TryGetValue( Get(), out unaryOperator ) )
		{
			Token operatorToken = Next();
			subexpr( unaryOperator.RightPriority );
			Expression operand = PopValue();

			SourceSpan s = new SourceSpan( operatorToken.SourceSpan.Start, operand.SourceSpan.End );
			Expression e = null;

			switch ( operatorToken.Kind )
			{
			case TokenKind.HyphenMinus:			e = new Unary( s, UnaryOp.Minus, operand );								break;
			case TokenKind.NumberSign:			e = new Unary( s, UnaryOp.Length, operand );							break;
			case TokenKind.Not:					e = new Not( s, operand );												break;
			}

			Debug.Assert( e != null );
			PushExpression( e );
		}
		else
		{
			simpleexp();
		}


		// Check for binary operators.

		Operator binaryOperator;
		while ( binaryOperators.TryGetValue( Get(), out binaryOperator ) && binaryOperator.LeftPriority > limit )
		{
			Expression left = PopValue();
			Token operatorToken = Next();
			subexpr( binaryOperator.RightPriority );
			Expression right = PopValue();

			SourceSpan s = new SourceSpan( left.SourceSpan.Start, right.SourceSpan.End );
			Expression e = null;
			
			switch ( operatorToken.Kind )
			{
			case TokenKind.PlusSign:			e = new Binary( s, BinaryOp.Add, left, right );							break;
			case TokenKind.HyphenMinus:			e = new Binary( s, BinaryOp.Subtract, left, right );					break;
			case TokenKind.Asterisk:			e = new Binary( s, BinaryOp.Multiply, left, right );					break;
			case TokenKind.Solidus:				e = new Binary( s, BinaryOp.Divide, left, right );						break;
			case TokenKind.ReverseSolidus:		e = new Binary( s, BinaryOp.IntegerDivide, left, right );				break;
			case TokenKind.PercentSign:			e = new Binary( s, BinaryOp.Modulus, left, right );						break;
			case TokenKind.CircumflexAccent:	e = new Binary( s, BinaryOp.RaiseToPower, left, right );				break;
			case TokenKind.Concatenate:			e = new Binary( s, BinaryOp.Concatenate, left, right );					break;

			case TokenKind.LogicalEqual:		e = new Comparison( s, ComparisonOp.Equal, left, right );				break;
			case TokenKind.NotEqual:			e = new Comparison( s, ComparisonOp.NotEqual, left, right );			break;
			case TokenKind.LessThanSign:		e = new Comparison( s, ComparisonOp.LessThan, left, right );			break;
			case TokenKind.GreaterThanSign:		e = new Comparison( s, ComparisonOp.GreaterThan, left, right );			break;
			case TokenKind.LessThanOrEqual:		e = new Comparison( s, ComparisonOp.LessThanOrEqual, left, right );		break;
			case TokenKind.GreaterThanOrEqual:	e = new Comparison( s, ComparisonOp.GreaterThanOrEqual, left, right );	break;

			case TokenKind.And:					e = new Logical( s, LogicalOp.And, left, right );						break;
			case TokenKind.Or:					e = new Logical( s, LogicalOp.Or, left, right );						break;
			}

			Debug.Assert( e != null );
			PushExpression( e );
		}
	}


	void simpleexp()
	{
		/*	simpleexp
				: primaryexp
				| STRING
				| NUMBER
				| constructor
				| functionexp
				| 'nil'
				| 'true'
				| 'false'
				| '...'
				;
		*/

		switch ( Get() )
		{
		case TokenKind.String:
			Token stringToken = Check( TokenKind.String );
			PushExpression( new Literal( stringToken.SourceSpan, stringToken.Value ) );
			break;

		case TokenKind.Number:
			Token numberToken = Check( TokenKind.Number );
			PushExpression( new Literal( stringToken.SourceSpan, numberToken.Value ) );
			break;

		case TokenKind.LeftCurlyBracket:
			constructor();
			break;

		case TokenKind.Function:
			functionexp();
			break;

		case TokenKind.Nil:
			Token nilToken = Check( TokenKind.Nil );
			PushExpression( new Literal( nilToken.SourceSpan, null ) );
			break;

		case TokenKind.True:
			Token trueToken = Check( TokenKind.True );
			PushExpression( new Literal( trueToken.SourceSpan, true ) );
			break;

		case TokenKind.False:
			Token falseToken = Check( TokenKind.False );
			PushExpression( new Literal( falseToken.SourceSpan, false ) );
			break;

		case TokenKind.Ellipsis:
			Token ellipsisToken = Check( TokenKind.Ellipsis );
			if ( function.IsVararg )
			{
				PushExpression( new Vararg( ellipsisToken.SourceSpan ) );
			}
			else
			{
				PushExpression( new Literal( ellipsisToken.SourceSpan, null ) );
				Error( "Cannot use '{0}' outside a vararg function", Lexer.GetTokenName( TokenKind.Ellipsis ) );
			}
			break;

		default:
			primaryexp();
			break;
		}
	}


	void constructor()
	{
		/*	constructor
				: '{' ( fieldlist )? '}' 
				;

			fieldlist
				: field ( fieldsep field )* fieldsep?
				;

			field
				: listfield
				| recfield
				;

			listfield
				: exp
				;

			recfield
				: IDENTIFIER '=' exp
				| '[' exp ']' '=' exp
				;

			fieldsep
				: ',' | ';'
				;
		*/

		Token matchConstructor = Check( TokenKind.LeftCurlyBracket );
		Constructor constructor = new Constructor( matchConstructor.SourceSpan );
		function.Statement( new BeginConstructor( matchConstructor.SourceSpan, constructor ) );

		int arrayKey = 1;

		while ( true )
		{
			if ( Get() == TokenKind.RightCurlyBracket )
				break;


			// field

			if ( Get() == TokenKind.Identifier && Lookahead() == TokenKind.EqualSign )
			{
				// recfield

				Token key = Check( TokenKind.Identifier );
				Token equalSign = Check( TokenKind.EqualSign );
				exp();
				Expression value = PopValue();

				constructor.IncrementHashCount();
				function.Statement(
					new Assign( new SourceSpan( key.SourceSpan.Start, value.SourceSpan.End ),
						new Index( key.SourceSpan,
							constructor,
							new Literal( key.SourceSpan, (string)key.Value ) ),
						value ) );
			}
			else if ( Get() == TokenKind.LeftSquareBracket )
			{
				// recfield

				Token leftBracket = Check( TokenKind.LeftSquareBracket );
				exp();
				Expression key = PopValue();
				Token rightBracket = Check( TokenKind.RightSquareBracket );
				Token equalSign = Check( TokenKind.EqualSign );
				exp();
				Expression value = PopValue();

				function.Statement(
					new Assign( new SourceSpan( leftBracket.SourceSpan.Start, value.SourceSpan.End ),
						new Index( new SourceSpan( leftBracket.SourceSpan.Start, rightBracket.SourceSpan.End ),
							constructor,
							key ),
						value ) );
			}
			else
			{
				// listfield

				Token token = GetToken();
				exp();
				if ( Get() != TokenKind.RightCurlyBracket && Lookahead() != TokenKind.RightCurlyBracket )
				{
					// normal field.

					Expression value = PopValue();

					constructor.IncrementArrayCount();
					function.Statement(
						new Assign( value.SourceSpan,
							new Index( value.SourceSpan,
								constructor,
								new Literal( value.SourceSpan, arrayKey ) ),
							value ) );

					arrayKey += 1;
				}
				else
				{
					// last field.

					Expression values = PopMultipleValues();
					if ( values == null )
					{
						Expression value = PopValue();

						constructor.IncrementArrayCount();
						function.Statement(
							new Assign( value.SourceSpan,
								new Index( value.SourceSpan,
									constructor,
									new Literal( value.SourceSpan, arrayKey ) ),
								value ) );
					}
					else
					{
						function.Statement( new AssignList( values.SourceSpan, constructor, arrayKey, values ) );
					}
				}
			}


			// fieldsep

			if ( Get() == TokenKind.Comma || Get() == TokenKind.Semicolon )
			{
				Next();
			}
			else
			{
				break;
			}
		}

		Token endConstructor = Check( TokenKind.RightCurlyBracket, matchConstructor );
		function.Statement( new EndConstructor( endConstructor.SourceSpan ) );
		constructor.SetSourceSpan( new SourceSpan( matchConstructor.SourceSpan.Start, endConstructor.SourceSpan.End ) );
		PushExpression( constructor );
	}


	void functionexp()
	{
		/*	functionexp
				: 'function' funcbody
				;
		*/

		Token matchFunction = Check( TokenKind.Function );

		funcbody( matchFunction, false );
	}


	ExpressionType primaryexp()
	{
		/*	primaryexp
				: prefixexp ( postfix )*	{ return result of last postfix, or false }
				;

			postfix
				: '.' IDENTIFIER
				| '[' exp ']'
				| funcargs					{ returns true }		
				| ':' IDENTIFIER funcargs	{ returns true }
				;
		*/

		ExpressionType expressionType = prefixexp();
		
		while ( true )
		{
			Token name;
			int argumentcount = 0;

			switch ( Get() )
			{
			// lookup.
			case TokenKind.FullStop:
			{
				expressionType = ExpressionType.Assignable;
				
				Expression table = PopValue();
				Token fullStop = Check( TokenKind.FullStop );
				name = Check( TokenKind.Identifier );
				
				SourceSpan s = new SourceSpan( table.SourceSpan.Start, name.SourceSpan.End );
				Expression key = new Literal( name.SourceSpan, (string)name.Value );

				PushExpression( new Index( s, table, key ) ); 
				break;
			}
				

			// array lookup.
			case TokenKind.LeftSquareBracket:
			{
				expressionType = ExpressionType.Assignable;
			
				Expression table = PopValue();
				Token leftBracket = Check( TokenKind.LeftSquareBracket );
				exp();
				Expression key = PopValue();
				Token rightBracket = Check( TokenKind.RightSquareBracket );

				SourceSpan s = new SourceSpan( table.SourceSpan.Start, rightBracket.SourceSpan.End );

				PushExpression( new Index( s, table, key ) );
				break;
			}

			// call
			case TokenKind.LeftParenthesis:
			case TokenKind.NewlineLeftParenthesis:
			case TokenKind.LeftCurlyBracket:
			case TokenKind.String:
			{
				expressionType = ExpressionType.FunctionCall;
			
				Expression function = PopValue();
				callexpr( function, null );
				
				break;
			}

			// selfcall
			case TokenKind.Colon:
			{
				expressionType = ExpressionType.FunctionCall;

				Expression function = PopValue();
				Check( TokenKind.Colon );
				name = Check( TokenKind.Identifier );
				callexpr( function, (string)name.Value );
				
				break;
			}

			// no more postfixes
			default:
			{
				return expressionType;
			}

			}

		}
	}


	void callexpr( Expression function, string methodName )
	{
		/*	funcargs
				: { no newline } '(' ( explist )? ')'
				| constructor
				| string
				;
		*/


		int argumentcount = 0;
		
		SourceLocation end;

		switch ( Get() )
		{
		case TokenKind.NewlineLeftParenthesis:
			// encountering this ambiguity is an error
			Error( "Newline between expression and function call arguments is ambiguous (remove newline or add a semicolon)" );
			// continue as if the newline didn't exist.
			goto case TokenKind.LeftParenthesis;

		case TokenKind.LeftParenthesis:
			Token matchParenthesis = Next();
			if ( Get() != TokenKind.RightParenthesis )
			{
				argumentcount = explist();
			}
			Token rightParenthesis = Check( TokenKind.RightParenthesis, matchParenthesis );
			end = rightParenthesis.SourceSpan.End;
			break;

		case TokenKind.LeftCurlyBracket:
			constructor();
			end = expression.Peek().SourceSpan.End;
			argumentcount = 1;
			break;
		
		case TokenKind.String:
			Token stringToken = Check( TokenKind.String );
			expression.Push( new Literal( stringToken.SourceSpan, stringToken.Value ) );
			end = stringToken.SourceSpan.End;
			argumentcount = 1;
			break;

		default:
			Error( "Function arguments expected" );
			break;
		}


		SourceSpan s = new SourceSpan( function.SourceSpan.Start, end );
	
		// Check for multiple results.
		Expression values = PopMultipleValues();
		if ( values != null )
		{
			argumentcount -= 1;
		}

		// Create a call or self-call expression.
		if ( methodName == null )
		{
			PushExpression( new Call( s, function, PopValues( argumentcount ), values ) );
		}
		else
		{
			PushExpression( new CallSelf( s, function, methodName, PopValues( argumentcount ), values ) );
		}
	}


	ExpressionType prefixexp()
	{
		/*	prefixexp
				: IDENTIFIER
				| '(' exp ')'
				;
		*/

		switch ( Get() )
		{
		case TokenKind.LeftParenthesis:
		case TokenKind.NewlineLeftParenthesis:
		{
			Token matchParenthesis = Next();
			exp();
			Token rightParenthesis = Check( TokenKind.RightParenthesis, matchParenthesis );

			Expression e = PopValue();
			e.SetSourceSpan( new SourceSpan( matchParenthesis.SourceSpan.Start, rightParenthesis.SourceSpan.End ) );
			PushExpression( new Nested( e.SourceSpan, e ) );
		
			return ExpressionType.None;
		}
			
		case TokenKind.Identifier:
		{
			Token nameToken = Check( TokenKind.Identifier );
			expression.Push( Lookup( nameToken ) );
			return ExpressionType.Assignable;
		}

		default:
		{
			expression.Push( new Literal( GetToken().SourceSpan, null ) );
			Error( "Unexpected token '{0}'", Lexer.GetTokenName( Get() ) );
			return ExpressionType.None;
		}

		}

	}





	// Scopes.

	enum BreakOrContinue
	{
		Invalid,
		Break,
		Continue,
	}

	struct BreakContinueAction
	{
		public BreakOrContinue		BreakOrContinue	{ get; private set; }
		public string				BlockName		{ get; private set; }


		public BreakContinueAction( BreakOrContinue breakOrContinue, string blockName )
			:	this()
		{
			BreakOrContinue	= breakOrContinue;
			BlockName		= blockName;
		}
	}
	
	class Scope
	{
		public Function				Function	{ get; private set; }
		public Scope				Parent		{ get; private set; }
		public IList< Variable >	Locals		{ get; private set; }
		public BreakContinueAction	Break		{ get; private set; }
		public BreakContinueAction	Continue	{ get; private set; }

		List< Variable > locals;


		public Scope( Function function, Scope parent )
			:	this( function, parent, new BreakContinueAction(), new BreakContinueAction() )
		{
		}

		public Scope( Function function, Scope parent, BreakContinueAction b, BreakContinueAction c )
		{
			Function	= function;
			Parent		= parent;
			locals		= new List< Variable >();
			Locals		= locals.AsReadOnly();
			Break		= b;
			Continue	= c;
		}

		public void Local( Variable local )
		{
			locals.Add( local );
		}
	}


	Expression Lookup( Token nameToken )
	{
		string name = (string)nameToken.Value;

		// Search through scopes.
		for ( Scope s = scope; s != null; s = s.Parent )
		{
			for ( int i = s.Locals.Count - 1; i >= 0; --i )
			{
				Variable variable = s.Locals[ i ];

				if ( variable.Name == name )
				{
					if ( s.Function == function )
					{
						// Is a local.
						return new Local( nameToken.SourceSpan, variable );
					}
					else
					{
						// Is an upval.
						variable.SetUpVal();
						return new UpVal( nameToken.SourceSpan, variable );
					}
				}
			}
		}

		// Is a global.
		return new Global( nameToken.SourceSpan, name );
	}




	// Converting expressions to single or multiple results.

	class Nested
		:	Expression
	{
		public Expression Expression { get; private set; }


		public Nested( SourceSpan s, Expression expression )
			:	base( s )
		{
			Expression = expression;
		}

	}


	void PushExpression( Expression e )
	{
		expression.Push( e );
	}

	
	Expression PopValue()
	{
		Expression value = expression.Pop();

		if ( value is Nested )
		{
			value = ( (Nested)value ).Expression;
		}

		if ( value is Vararg )
		{
			value = new VarargElement( value.SourceSpan, 0 );
		}

		return value;
	}


	IList< Expression > PopValues( int count )
	{
		Expression[] result = new Expression[ count ];

		for ( int i = count - 1; i >= 0; --i )
		{
			result[ i ] = PopValue();
		}

		return Array.AsReadOnly( result );
	}


	Expression PopMultipleValues()
	{
		Expression value = expression.Pop();

		if ( value is Call )
		{
			return value;
		}

		if ( value is CallSelf )
		{
			return value;
		}

		if ( value is Vararg )
		{
			return value;
		}

		expression.Push( value );
		return null;
	}




	
	// Token reading.

	TokenKind Get()
	{
		return GetToken().Kind;
	}

	Token GetToken()
	{
		return token;
	}

	TokenKind Lookahead()
	{
		return LookaheadToken().Kind;
	}

	Token LookaheadToken()
	{
		if ( lookahead.Kind == TokenKind.None )
		{
			lookahead = lexer.ReadToken();
		}
		return lookahead;
	}
	
	Token Next()
	{
		Token current = token;

		if ( lookahead.Kind == TokenKind.None )
		{
			token = lexer.ReadToken();
		}
		else
		{
			token		= lookahead;
			lookahead	= new Token();
		}
		
		return current;
	}

	bool Test( TokenKind kind )
	{
		if ( Get() == kind )
		{
			Next();
			return true;
		}
		return false;
	}

	Token Check( TokenKind kind )
	{
		if ( Get() != kind )
		{
			Error( "'{0}' expected", Lexer.GetTokenName( kind ) );
			return GetToken();
		}

		return Next();
	}

	Token Check( TokenKind kind, Token matchTo )
	{
		if ( Get() != kind )
		{
			Error( "'{0}' expected to close", Lexer.GetTokenName( kind ) );
			Error( matchTo.SourceSpan.Start, "    '{0}' here", Lexer.GetTokenName( matchTo.Kind ) );
			return GetToken();
		}

		return Next();
	}



	// Error reporting.

	void Error( string format, params object[] args )
	{
		Error( token.SourceSpan.Start, format, args );
	}

	void Error( SourceLocation l, string format, params object[] args )
	{
		Console.Error.WriteLine( "{0}({1},{2}): error: {3}",
			l.SourceName, l.Line, l.Column, System.String.Format( format, args ) );
		hasError = true;
	}



	
	// Parse stack.

	class ParseStack< T >
	{
		List< T >	list;
		int			top;

		public ParseStack()
		{
			list	= new List< T >();
			top		= 0;
		}

		public int Count
		{
			get { return top; }
		}

		public void Push( T item )
		{
			list.RemoveRange( top, list.Count - top );
			list.Add( item );
			top = list.Count;
		}

		public T Pop()
		{
			top -= 1;
			return list[ top ];
		}

		public IList< T > Pop( int count )
		{
			top -= count;
			return new StackSlice( list, top, count );
		}

		public T Peek()
		{
			return list[ top - 1 ];
		}

		public T Peek( int count )
		{
			return list[ top - 1 - count ];
		}


		// Access to a subrange of the stack, without copying it.

		class StackSlice
			:	IList< T >
		{
			List< T >	list;
			int			start;
			int			count;

			public int	Count		{ get { return count; } }
			public bool	IsReadOnly	{ get { return true; } }

			public T this[ int index ]
			{
				get { return list[ start + index ]; }
				set { list[ start + index ] = value; }
			}
			
			public StackSlice( List< T > list, int start, int count )
			{
				Debug.Assert( start >= 0 );
				Debug.Assert( start + count <= list.Count );

				this.list	= list;
				this.start	= start;
				this.count	= count;
			}

			public int IndexOf( T item )
			{
				return list.IndexOf( item, start, count );
			}

			public bool Contains( T item )
			{
				return list.Contains( item );
			}

			public void CopyTo( T[] array, int arrayIndex )
			{
				list.CopyTo( start, array, arrayIndex, count );
			}

			public IEnumerator<T> GetEnumerator()
			{
				for ( int i = 0; i < count; ++i )
				{
					yield return this[ i ];
				}
			}

			System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
			{
				return GetEnumerator();
			}

			public void Insert( int index, T item )	{ throw new NotSupportedException( "Collection is read-only" ); }
			public void RemoveAt( int index )		{ throw new NotSupportedException( "Collection is read-only" ); }
			public void Add( T item )				{ throw new NotSupportedException( "Collection is read-only" ); }
			public void Clear()						{ throw new NotSupportedException( "Collection is read-only" ); }
			public bool Remove( T item )			{ throw new NotSupportedException( "Collection is read-only" ); }

		}

	}


}


}

