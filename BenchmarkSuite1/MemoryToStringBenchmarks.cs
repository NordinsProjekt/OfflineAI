using System.Text;
using BenchmarkDotNet.Attributes;
using MemoryLibrary;
using MemoryLibrary.Models;
using OfflineAI;
using Services;

public class MemoryToStringBenchmarks
{
 private SimpleMemory simpleMemory;
 private Gloomhaven gloom;
 private IMemoryFragment[] fragments;

 [GlobalSetup]
 public void Setup()
 {
 // Create a set of fragments so both memories have the same amount of text
 fragments = new IMemoryFragment[100];
 for (int i =0; i <100; i++)
 {
 var content = new StringBuilder();
 for (int j =0; j <200; j++) content.Append('x');
 fragments[i] = new MemoryFragment($"Title {i}", content.ToString());
 }

 simpleMemory = new SimpleMemory();
 gloom = new Gloomhaven();

 // Replace internal Memory list of Gloomhaven via reflection
 var memField = typeof(Gloomhaven).GetField("Memory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
 if (memField != null)
 {
 var list = new System.Collections.Generic.List<IMemoryFragment>();
 foreach (var f in fragments) list.Add(f);
 memField.SetValue(gloom, list);
 }

 foreach (var f in fragments) simpleMemory.ImportMemory(f);
 }

 [Benchmark]
 public string ToString_SimpleMemory()
 {
 return simpleMemory.ToString();
 }

 [Benchmark]
 public string ToString_Gloomhaven()
 {
 return gloom.ToString();
 }
}
