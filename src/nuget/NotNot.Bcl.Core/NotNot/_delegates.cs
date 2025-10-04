// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

namespace NotNot;

/// <summary>
/// A Func where all parameters and the return value are passed by reference
/// </summary>
public delegate ref T Func_Ref<T>();

/// <summary>
/// A Func where all parameters but NOT the return value are passed by reference
/// </summary>
public delegate TReturn Func_RefArg<T1, TReturn>(ref T1 arg1);
public delegate TReturn Func_RefArg<T1, T2, TReturn>(ref T1 arg1, ref T2 arg2);

/// <summary>
/// A Func where all parameters and the return value are passed by reference
/// </summary>
public delegate ref TReturn Func_Ref<T1, TReturn>(ref T1 arg1);
public delegate ref TReturn Func_Ref<T1, T2, TReturn>(ref T1 arg1, ref T2 arg2);

public delegate void Action_Span<T>(Span<T> span);

public delegate void Action_RoSpan<TSpan>(ReadOnlySpan<TSpan> span);

public delegate void Action_RoSpan<TSpan, TArg>(ReadOnlySpan<TSpan> span, TArg arg);

/// <summary>
///    action where all parameters are passed by reference
/// </summary>
/// <typeparam name="T1"></typeparam>
/// <param name="val1"></param>
public delegate void Action_Ref<T>(ref T arg);

public delegate void Action_Ref<T1, T2>(ref T1 arg1, ref T2 arg2);

public delegate void Action_Ref<T1, T2, T3>(ref T1 arg1, ref T2 arg2, ref T3 arg3);

//public delegate TResult Func_Typed<TResult>()
