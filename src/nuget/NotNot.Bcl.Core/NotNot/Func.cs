// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NotNot;

public static partial class Func
{
   /// <summary>
   /// explicit evaluation of a Func
   /// </summary>
   /// <typeparam name="T"></typeparam>
   /// <param name="toEval"></param>
   /// <returns></returns>
   public static T Eval<T>(Func<T> toEval)
   {
      return toEval();
   }

   public static TOut Eval<TIn, TOut>(Func<TIn, TOut> toEval, TIn input)
   {
      return toEval(input);
   }

}

