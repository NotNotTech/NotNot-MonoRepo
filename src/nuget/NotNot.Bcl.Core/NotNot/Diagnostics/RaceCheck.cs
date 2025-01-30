// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] By default, this file is licensed to you under the AGPL-3.0.
// [!!] However a Private Commercial License is available. 
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] ------------------------------------------------- 
// [!!] Contributions Guarantee Citizenship! 
// [!!] Would you like to know more? https://github.com/NotNotTech/NotNot 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

namespace NotNot.Diagnostics;

//}

///// <summary>
///// cheap non-blocking way to track resource availabiltiy
///// <para></para>
///// </summary>
//[ThreadSafety(ThreadSituation.Never)]
//public class DebugReadWriteCounter
//{
//    public int _writes;
//    public int _reads;
//    private int _version;

//    public bool IsReadHeld { get { return _reads > 0; } }
//    public bool IsWriteHeld { get { return _writes > 0; } }
//    public bool IsAnyHeld { get => IsReadHeld || IsWriteHeld; }

//    public void EnterWrite()
//    {
//        _version++;
//        var ver = _version;
//        __.GetLogger()._EzError(IsAnyHeld == false, "a lock already held");
//        _writes++;
//        __.GetLogger()._EzError(_writes == 1, "writes out of balance");
//        __.GetLogger()._EzError(ver == _version);
//    }
//    public void ExitWrite()
//    {
//        _version++;
//        var ver = _version;
//        _writes--;
//        __.GetLogger()._EzError(_writes == 0, "writes out of balance");
//        __.GetLogger()._EzError(ver == _version);
//    }
//    public void EnterRead()
//    {
//        _version++;
//        var ver = _version;
//        __.GetLogger()._EzError(IsWriteHeld == false, "write lock already held");
//        _reads++;
//        __.GetLogger()._EzError(_reads > 0, "reads out of balance");
//        __.GetLogger()._EzError(ver == _version);
//    }
//    public void ExitRead()
//    {
//        _version++;
//        var ver = _version;
//        _reads--;
//        __.GetLogger()._EzError(_reads >= 0, "reads out of balance");
//        __.GetLogger()._EzError(ver == _version);
//    }

//}

/// <summary>
///    cheap non-blocking way to diagnose resource read-write style race conditions.
///    if you need lock style behavior (not read/write style), use <see cref="RaceCheckSlim" />
/// </summary>
/// <remarks>for diagnostics purposes, doesn't actually help synchronize, just informs when race conditions occur.</remarks>
public class RaceCheck
{
   public volatile int _reads;
   private volatile int _version;
   public volatile int _writes;

   public RaceCheck(bool singleWriter, bool singleReader = false)
   {
      SingleReader = singleReader;
      SingleWriter = singleWriter;
   }


   public bool IsReadHeld => _reads > 0;
   public bool IsWriteHeld => _writes > 0;
   public bool IsAnyHeld => IsReadHeld || IsWriteHeld;

   public bool SingleReader { get; init; }
   public bool SingleWriter { get; init; }

   public WriteDisposable EnterWrite()
   {
      var ver = Interlocked.Increment(ref _version);
      __.GetLogger()._EzError(IsReadHeld is false, "a lock already held");
      var writes = Interlocked.Increment(ref _writes);
      if (SingleWriter)
      {
         __.GetLogger()._EzError(writes == 1, "writes out of balance");
         __.GetLogger()._EzError(ver == _version, "single write allowed.  version out of balance");
      }

      return new WriteDisposable(this);
   }

   public void ExitWrite()
   {
      var ver = Interlocked.Increment(ref _version);
      __.GetLogger()._EzError(IsReadHeld is false, "a read lock still held");

      var writes = Interlocked.Decrement(ref _writes);
      if (SingleWriter)
      {
         __.GetLogger()._EzError(writes == 0, "writes out of balance");
         __.GetLogger()._EzError(ver == _version, "single write allowed.  version out of balance");
      }
      else
      {
         __.GetLogger()._EzError(writes >= 0, "writes out of balance");
      }
   }

   public ReadDisposable EnterRead()
   {
      var ver = Interlocked.Increment(ref _version);
      __.GetLogger()._EzError(IsWriteHeld == false, "write lock already held");
      var reads = Interlocked.Increment(ref _reads);

      if (SingleReader)
      {
         __.GetLogger()._EzError(reads == 1, "single reader.  lock already held");
         __.GetLogger()._EzError(ver == _version, "single read allowed.  version out of balance");
      }
      else
      {
         __.GetLogger()._EzError(_reads > 0, "reads out of balance");
      }

      return new ReadDisposable(this);
   }

   public void ExitRead()
   {
      var ver = Interlocked.Increment(ref _version);
      var reads = Interlocked.Decrement(ref _reads);
      if (SingleReader)
      {
         __.GetLogger()._EzError(IsAnyHeld == false, "single reader.  lock already held");
         __.GetLogger()._EzError(ver == _version, "single read allowed.  version out of balance");
      }
      else
      {
         __.GetLogger()._EzError(IsWriteHeld == false, "write lock already held");
         __.GetLogger()._EzError(reads >= 0, "reads out of balance");
      }
   }

   /// <summary>
   ///    allows utilization of the `using` pattern to auto exit the Read.  example: `using counter.EnterRead(){  /*your
   ///    code*/ }`
   ///    <para>ignore if you manually decrement</para>
   /// </summary>
   public record struct ReadDisposable(RaceCheck parent) : IDisposable
   {
      public void Dispose()
      {
         parent.ExitRead();
      }
   }

   /// <summary>
   ///    allows utilization of the `using` pattern to auto exit the Write.
   ///    <para>ignore if you manually decrement</para>
   /// </summary>
   public record struct WriteDisposable(RaceCheck parent) : IDisposable
   {
      public void Dispose()
      {
         parent.ExitWrite();
      }
   }
}