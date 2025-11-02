// Quick verification test for ObjectPool auto-clear functionality
// Compile and run: dotnet script test-objectpool-autoclear.cs

using System;
using System.Collections.Generic;
using NotNot._internal;

Console.WriteLine("ObjectPool Auto-Clear Verification Test");
Console.WriteLine("========================================\n");

// Test 1: Return_New with List auto-clear
Console.WriteLine("Test 1: List auto-clear");
var pool = new ObjectPool();
var list = pool.Get<List<int>>();
list.Add(1);
list.Add(2);
list.Add(3);
Console.WriteLine($"  Before return: Count = {list.Count}");
pool.Return_New(list);
var reused = pool.Get<List<int>>();
Console.WriteLine($"  After return: Count = {reused.Count} (should be 0)");
Console.WriteLine($"  Same instance: {object.ReferenceEquals(list, reused)} (should be True)");
Console.WriteLine($"  ✓ Test 1: {(reused.Count == 0 && object.ReferenceEquals(list, reused) ? "PASS" : "FAIL")}\n");

// Test 2: Return_New with skipAutoClear=true
Console.WriteLine("Test 2: Skip auto-clear (preserve state)");
var list2 = pool.Get<List<int>>();
list2.Add(10);
list2.Add(20);
Console.WriteLine($"  Before return: Count = {list2.Count}");
pool.Return_New(list2, skipAutoClear: true);
var reused2 = pool.Get<List<int>>();
Console.WriteLine($"  After return: Count = {reused2.Count} (should be 2)");
Console.WriteLine($"  ✓ Test 2: {(reused2.Count == 2 ? "PASS" : "FAIL")}\n");

// Test 3: Rented<T> with auto-clear
Console.WriteLine("Test 3: Rented<T> default behavior");
using (var rented = pool.Rent<Dictionary<string, int>>())
{
    rented.Value.Add("a", 1);
    rented.Value.Add("b", 2);
    Console.WriteLine($"  Inside using: Count = {rented.Value.Count}");
} // Dispose calls Return_New with skipAutoClear=false
var dict = pool.Get<Dictionary<string, int>>();
Console.WriteLine($"  After using block: Count = {dict.Count} (should be 0)");
Console.WriteLine($"  ✓ Test 3: {(dict.Count == 0 ? "PASS" : "FAIL")}\n");

// Test 4: StaticPool auto-clear
Console.WriteLine("Test 4: StaticPool auto-clear");
var staticPool = new StaticPool();
var hashSet = staticPool.Get<HashSet<string>>();
hashSet.Add("test1");
hashSet.Add("test2");
Console.WriteLine($"  Before return: Count = {hashSet.Count}");
staticPool.Return_New(hashSet);
var reusedSet = staticPool.Get<HashSet<string>>();
Console.WriteLine($"  After return: Count = {reusedSet.Count} (should be 0)");
Console.WriteLine($"  Same instance: {object.ReferenceEquals(hashSet, reusedSet)} (should be True)");
Console.WriteLine($"  ✓ Test 4: {(reusedSet.Count == 0 && object.ReferenceEquals(hashSet, reusedSet) ? "PASS" : "FAIL")}\n");

// Test 5: Type without Clear method (no-op)
Console.WriteLine("Test 5: Type without Clear() method");
var noCleanObj = pool.Get<MyClassWithoutClear>();
noCleanObj.Value = 42;
pool.Return_New(noCleanObj); // Should not throw
var reusedObj = pool.Get<MyClassWithoutClear>();
Console.WriteLine($"  Value preserved: {reusedObj.Value} (should be 42)");
Console.WriteLine($"  ✓ Test 5: {(reusedObj.Value == 42 ? "PASS" : "FAIL")}\n");

Console.WriteLine("========================================");
Console.WriteLine("All tests completed!");

class MyClassWithoutClear
{
    public int Value { get; set; }
}
