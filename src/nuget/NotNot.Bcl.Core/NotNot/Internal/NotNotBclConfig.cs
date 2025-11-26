// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

namespace NotNot.Internal;

/// <summary>
///    default values go here
/// </summary>
public static class NotNotBclConfig
{
	public const int Allocator_maxAllocatorDefault = 1000;
	public const int ResizableArray_minShrinkSize = 100;
	/// <summary>
	/// certain runtimes like godot do weird shutdown logic.  this is a hint that such a shutdown is in progress, and we should do things like avoid throwing exceptions in destructors
	/// </summary>
	public static bool IsAppShutdownInProgress = false;
}
