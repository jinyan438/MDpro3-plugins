using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Test-only transport adapter. Rule processing, legal prompts and AI code remain
// the actual game assembly. This copy is never staged into the playable client.
internal static class StoryCoreTransport
{
    private static void Main(string[] args)
    {
        var resolver = new DefaultAssemblyResolver();
        foreach (string path in File.ReadAllLines(args[2])) resolver.AddSearchDirectory(Path.GetDirectoryName(path));
        using (var assembly = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { AssemblyResolver = resolver }))
        {
            var module = assembly.MainModule;
            var client = module.GetType("YGOSharp.Network.YGOClient");
            var sink = new FieldDefinition("StoryTestSend", FieldAttributes.Public | FieldAttributes.Static,
                module.ImportReference(typeof(Action<object, byte[]>)));
            client.Fields.Add(sink);
            var send = client.Methods.Single(m => m.Name == "Send" && m.Parameters.Count == 1 &&
                m.Parameters[0].ParameterType.FullName == "System.IO.BinaryWriter");
            send.Body = new MethodBody(send);
            var il = send.Body.GetILProcessor();
            il.Emit(OpCodes.Ldsfld, sink);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, module.ImportReference(typeof(BinaryWriter).GetProperty("BaseStream").GetGetMethod()));
            il.Emit(OpCodes.Castclass, module.ImportReference(typeof(MemoryStream)));
            il.Emit(OpCodes.Callvirt, module.ImportReference(typeof(MemoryStream).GetMethod("ToArray")));
            il.Emit(OpCodes.Callvirt, module.ImportReference(typeof(Action<object, byte[]>).GetMethod("Invoke")));
            il.Emit(OpCodes.Ret);
            assembly.Write(args[1]);
        }
    }
}
