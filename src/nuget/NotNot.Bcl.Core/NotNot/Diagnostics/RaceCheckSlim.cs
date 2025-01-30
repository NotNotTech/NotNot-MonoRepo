// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using NotNot.Collections._unused;
using System.Diagnostics;

namespace NotNot.Diagnostics;

/// <summary>
///    Debug helper class for ensuring race-conditions do not occur.  Use this to ensure your algorithms are correct.
///    <para>Throws if <see cref="Enter" /> is called multiple times without first calling <see cref="Exit" /></para>
///    <para>
///       useful for multithread race condition checking, recursive method re-entry protection, state corruption
///       protection
///    </para>
///    <para>only enabled in #CHECKED builds (causes no overhead otherwise)</para>
///    <para>
///       API and usage pattern is similar to a ReaderWriterLockSlim (RWLS).  Use RWLS to ensure thread safety.  use
///       This RaceCheck to just 'check' for thread safety
///    </para>
/// </summary>
/// <remarks>for diagnostics purposes, doesn't actually help synchronize, just informs when race conditions occur.</remarks>
[DebuggerNonUserCode]
public struct RaceCheckSlim
{
   //#if DEBUG || THREAD_DIAG
   private int version;

   private int lockVersion;


   public Char10 _debugNote;

   [Conditional("CHECKED")]
   public void Enter()
   {
      if (lockVersion != 0)
      {
         //set lock version to -1 to cause the other thread caller to assert also
         lockVersion = -1;
         __.GetLogger()._EzErrorThrow(false,
            $"RaceCheck.Enter() failed!  Something else has this locked!  Additional Info={_debugNote}");
         return;
      }

      lockVersion = version;
      version++;
   }

   [Conditional("CHECKED")]
   public void Edit()
   {
      Enter();
      Exit();
   }

   [Conditional("CHECKED")]
   public void Exit()
   {
      if (lockVersion != version - 1)
      {
         if (lockVersion == 0)
         {
            __.GetLogger()._EzErrorThrow(false,
               $"RaceCheck.Exit() failed!  Already unlocked and we are calling unlock again!  Additional Info={_debugNote}");
         }
         else
         {
            __.GetLogger()._EzErrorThrow(false,
               $"RaceCheck.Exit() failed!  internal state is corrupted, moste likely from multithreading  Additional Info={_debugNote}");
         }

         //cause the assert to keep occuring if this failed
         return;
      }

      lockVersion = 0;
   }

   public override string ToString()
   {
#if CHECKED
		var isEdit = lockVersion == 0 ? false : true;
		return "version=" + version.ToString() + " isBeingEdited=" + isEdit.ToString();// +ParseHelper.FormatInvariant(" Additional DebugText=\"{0}\"", _debugText);
#else
      return "RaceCheck is DISABLED.  to enable run in #CHECKED builds.";
#endif
   }

   /// <summary>
   ///    akin to ReaderWriterLockSlim.Read().  Ensure that no Lock is being held.
   /// </summary>
   [Conditional("CHECKED")]
   internal void Poke()
   {
      version++;
      __.GetLogger()._EzErrorThrow(lockVersion == 0,
         $"RaceCheck.Exit() failed!  internal state is corrupted, moste likely from multithreading  Additional Info={_debugNote}");
   }
}