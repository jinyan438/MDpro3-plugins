using System;
using System.IO;
using System.Linq;
using MDPro3.Plugins.CodeGen;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

internal sealed class StoryCompiledAssembly : ICompiledAssembly
{
    public string Name => "Assembly-CSharp";
    public string[] References { get; set; }
    public string[] Defines => new string[0];
    public InMemoryAssembly InMemoryAssembly { get; set; }
}

internal static class StoryEditorWeaverTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Main(string[] args)
    {
        var pdb = Path.ChangeExtension(args[0], ".pdb");
        var input = new StoryCompiledAssembly { References = File.ReadAllLines(args[2]),
            InMemoryAssembly = new InMemoryAssembly(File.ReadAllBytes(args[0]), File.Exists(pdb) ? File.ReadAllBytes(pdb) : new byte[0]) };
        var processor = new StoryEditorPostProcessor();
        Check(processor.WillProcess(input), "process game assembly");
        var result = processor.Process(input);
        Check(result != null && !result.Diagnostics.Any(d => d.DiagnosticType == DiagnosticType.Error),
            string.Join("\n", result?.Diagnostics.Select(d => d.MessageData) ?? new[] { "no output" }));
        Check(result.InMemoryAssembly != null, "postprocessor emits assembly");
        using (var patched = AssemblyDefinition.ReadAssembly(new MemoryStream(result.InMemoryAssembly.PeData)))
        {
            var calls = patched.MainModule.Types.SelectMany(t => t.Methods).Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions)
                .Count(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference r
                    && r.DeclaringType.Name == "StoryDeckEditorHooks");
            Check(calls >= 15, "all native guards inserted");
            var roomShow = patched.MainModule.GetType("MDPro3.Servant.RoomServant").Methods
                .Single(m => m.Name == "ApplyShowArrangement" && m.Parameters.Count == 1);
            Check(roomShow.Body.Instructions.Count(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference r
                && r.DeclaringType.Name == "StoryDeckEditorHooks" && r.Name == "ShowRoomChat") == 1,
                "story-aware room chat hook inserted");
            Check(!StoryEditorWeaver.Apply(patched.MainModule), "weaving is idempotent");
        }
        File.WriteAllBytes(args[1], result.InMemoryAssembly.PeData);
        if (result.InMemoryAssembly.PdbData.Length > 0) File.WriteAllBytes(Path.ChangeExtension(args[1], ".pdb"), result.InMemoryAssembly.PdbData);

        // A changed base-game entry point must fail the build rather than ship bypassable rules.
        using (var resolver = new DefaultAssemblyResolver())
        {
            foreach (var directory in input.References.Select(Path.GetDirectoryName).Distinct()) resolver.AddSearchDirectory(directory);
            using (var changed = AssemblyDefinition.ReadAssembly(new MemoryStream(input.InMemoryAssembly.PeData), new ReaderParameters { AssemblyResolver = resolver }))
            {
                changed.MainModule.GetType("MDPro3.UI.DeckView").Methods.First(m => m.Name == "CanAddCard").Name = "ChangedAPI";
                using (var output = new MemoryStream())
                {
                    changed.Write(output);
                    input.InMemoryAssembly = new InMemoryAssembly(output.ToArray(), new byte[0]);
                    var rejected = processor.Process(input);
                    Check(rejected.Diagnostics.Any(d => d.DiagnosticType == DiagnosticType.Error), "unsupported API rejected");
                }
            }
        }
        Console.WriteLine("Native editor IL postprocessor: PASS (injection, symbols, idempotency, API mismatch rejection)");
    }
}
