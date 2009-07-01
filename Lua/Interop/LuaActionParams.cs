﻿// LuaActionParams.cs
//
// Lua 5.1 is copyright © 1994-2008 Lua.org, PUC-Rio, released under the MIT license
// LuaCLR is copyright © 2007-2008 Fabio Mascarenhas, released under the MIT license
// This version copyright © 2009 Edmund Kapusniak


using System;


namespace Lua.Interop
{


public delegate void ActionParams< TParams >( params TParams[] arguments );
public delegate void ActionParams< T, TParams >( T a1, params TParams[] arguments );
public delegate void ActionParams< T1, T2, TParams >( T1 a1, T2 a2, params TParams[] arguments );
public delegate void ActionParams< T1, T2, T3, TParams >( T1 a1, T2 a2, T3 a3, params TParams[] arguments );
public delegate void ActionParams< T1, T2, T3, T4, TParams >( T1 a1, T2 a2, T3 a3, T4 a4, params TParams[] arguments );


}



