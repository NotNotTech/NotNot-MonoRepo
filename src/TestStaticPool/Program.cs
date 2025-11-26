// Quick test to verify StaticPool refactor works correctly

using System;
using System.Collections.Generic;
using NotNot._internal;

class TestStaticPoolRefactor
{
    static void Main()
    {
        Console.WriteLine("Testing StaticPool refactor to wrap ObjectPool");
        Console.WriteLine("===============================================\n");

        int failureCount = 0;

        // Test 1: Basic Get and Return (no clear)
        Console.WriteLine("Test 1: Legacy Return (no auto-clear)");
        var list1 = StaticPool.Get<List<int>>();
        list1.Add(100);
        list1.Add(200);
        StaticPool.Return(list1); // Legacy Return - should NOT clear
        var reused1 = StaticPool.Get<List<int>>();
        bool test1Pass = ReferenceEquals(list1, reused1) && reused1.Count == 2;
        Console.WriteLine($"  Same instance: {ReferenceEquals(list1, reused1)}");
        Console.WriteLine($"  Count preserved: {reused1.Count} (expected 2)");
        Console.WriteLine($"  Result: {(test1Pass ? "PASS" : "FAIL")}\n");
        if (!test1Pass) failureCount++;

        // Test 2: Return_New with auto-clear
        Console.WriteLine("Test 2: Return_New (with auto-clear)");
        var list2 = StaticPool.Get<List<int>>();
        list2.Add(300);
        list2.Add(400);
        StaticPool.Return_New(list2); // Should clear
        var reused2 = StaticPool.Get<List<int>>();
        bool test2Pass = ReferenceEquals(list2, reused2) && reused2.Count == 0;
        Console.WriteLine($"  Same instance: {ReferenceEquals(list2, reused2)}");
        Console.WriteLine($"  Count cleared: {reused2.Count} (expected 0)");
        Console.WriteLine($"  Result: {(test2Pass ? "PASS" : "FAIL")}\n");
        if (!test2Pass) failureCount++;

        // Test 3: Return_New with skipAutoClear
        Console.WriteLine("Test 3: Return_New with skipAutoClear=true");
        var list3 = StaticPool.Get<List<int>>();
        list3.Add(500);
        StaticPool.Return_New(list3, skipAutoClear: true); // Should NOT clear
        var reused3 = StaticPool.Get<List<int>>();
        bool test3Pass = ReferenceEquals(list3, reused3) && reused3.Count == 1;
        Console.WriteLine($"  Same instance: {ReferenceEquals(list3, reused3)}");
        Console.WriteLine($"  Count preserved: {reused3.Count} (expected 1)");
        Console.WriteLine($"  Result: {(test3Pass ? "PASS" : "FAIL")}\n");
        if (!test3Pass) failureCount++;

        // Test 4: Arrays
        Console.WriteLine("Test 4: Array pooling");
        var arr1 = StaticPool.GetArray<int>(5);
        arr1[0] = 10;
        arr1[4] = 50;
        StaticPool.ReturnArray(arr1, preserveContents: false); // Should clear
        var reusedArr = StaticPool.GetArray<int>(5);
        bool test4Pass = ReferenceEquals(arr1, reusedArr) && reusedArr[0] == 0 && reusedArr[4] == 0;
        Console.WriteLine($"  Same instance: {ReferenceEquals(arr1, reusedArr)}");
        Console.WriteLine($"  Array cleared: arr[0]={reusedArr[0]}, arr[4]={reusedArr[4]} (expected 0, 0)");
        Console.WriteLine($"  Result: {(test4Pass ? "PASS" : "FAIL")}\n");
        if (!test4Pass) failureCount++;

        // Test 5: Globally shared pool
        Console.WriteLine("Test 5: Globally shared static pool");
        var dict = StaticPool.Get<Dictionary<string, int>>();
        dict["test"] = 123;
        StaticPool.Return(dict); // Put in shared pool
        var retrieved = StaticPool.Get<Dictionary<string, int>>();
        bool test5Pass = ReferenceEquals(dict, retrieved);
        Console.WriteLine($"  Same instance retrieved from static pool: {ReferenceEquals(dict, retrieved)}");
        Console.WriteLine($"  Result: {(test5Pass ? "PASS" : "FAIL")}\n");
        if (!test5Pass) failureCount++;

        // Summary
        Console.WriteLine("===============================================");
        Console.WriteLine($"Total tests: 5, Passed: {5 - failureCount}, Failed: {failureCount}");
        if (failureCount == 0)
        {
            Console.WriteLine("SUCCESS: All tests passed!");
        }
        else
        {
            Console.WriteLine($"FAILURE: {failureCount} test(s) failed.");
            Environment.Exit(1);
        }
    }
}