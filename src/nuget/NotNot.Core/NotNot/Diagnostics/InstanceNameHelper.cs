// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]


// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]
// [!!] Copyright ©️ NotNot Project and Contributors.
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info.
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

namespace NotNot.Diagnostics;

/// <summary>
///    simple static helper to provide unique named instances of types.
///    <para>For example, calling .CreateName{int}() ==> "int_0".   Calling it again would return "int_1" </para>
/// </summary>
[ThreadSafety(ThreadSituation.Always)]
public static class InstanceNameHelper
{
   private static Dictionary<string, ulong> _countTracker = new();

   /// <summary>
   ///    uses Type.Name, eg return: "Int_42"
   /// </summary>
   public static string CreateName<T>()
   {
      var type = typeof(T);
      var name = type.Name;
      lock (_countTracker)
      {
         ref var counter = ref _countTracker._GetValueRefOrAddDefault_Unsafe(name, out _);
         return $"{name}_{counter++}";
      }
   }

   /// <summary>
   ///    uses Type.Name, eg return: "Int_42"
   /// </summary>
   public static string CreateName(Type type)
   {
      var name = type.Name;
      lock (_countTracker)
      {
         ref var counter = ref _countTracker._GetValueRefOrAddDefault_Unsafe(name, out _);
         return $"{name}_{counter++}";
      }
   }

   /// <summary>
   ///    uses Type.FullName, eg: "System.Int_42"
   /// </summary>
   public static string CreateNameFull<T>()
   {
      var type = typeof(T);
      var name = type.FullName;
      lock (_countTracker)
      {
         ref var counter = ref _countTracker._GetValueRefOrAddDefault_Unsafe(name, out _);
         return $"{name}_{counter++}";
      }
   }
}