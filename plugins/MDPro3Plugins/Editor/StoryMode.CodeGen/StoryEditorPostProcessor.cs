using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace MDPro3.Plugins.CodeGen
{
    // Unity runs this for editor and player compilations before managed stripping.
    // Only compiled IL is amended; no host-game source or prefab is rewritten.
    public sealed class StoryEditorPostProcessor : ILPostProcessor
    {
        public override ILPostProcessor GetInstance() => new StoryEditorPostProcessor();
        public override bool WillProcess(ICompiledAssembly assembly) => assembly.Name == "Assembly-CSharp";
        public override ILPostProcessResult Process(ICompiledAssembly compiled)
        {
            if (!WillProcess(compiled)) return null;
            var diagnostics = new List<DiagnosticMessage>();
            try
            {
                using (var resolver = new DefaultAssemblyResolver())
                {
                    foreach (string path in compiled.References.Select(Path.GetDirectoryName).Distinct()) resolver.AddSearchDirectory(path);
                    var symbols = compiled.InMemoryAssembly.PdbData;
                    bool hasSymbols = symbols != null && symbols.Length > 0;
                    var read = new ReaderParameters { AssemblyResolver = resolver, ReadSymbols = hasSymbols };
                    if (hasSymbols) { read.SymbolReaderProvider = new PortablePdbReaderProvider(); read.SymbolStream = new MemoryStream(symbols); }
                    using (var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(compiled.InMemoryAssembly.PeData), read))
                    {
                        if (!StoryEditorWeaver.Apply(assembly.MainModule)) return null;
                        using (var pe = new MemoryStream())
                        using (var pdb = new MemoryStream())
                        {
                            var write = new WriterParameters { WriteSymbols = hasSymbols };
                            if (hasSymbols) { write.SymbolWriterProvider = new PortablePdbWriterProvider(); write.SymbolStream = pdb; }
                            assembly.Write(pe, write);
                            return new ILPostProcessResult(new InMemoryAssembly(pe.ToArray(), pdb.ToArray()), diagnostics);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Never ship a build that silently removes collection/save safeguards.
                diagnostics.Add(new DiagnosticMessage { DiagnosticType = DiagnosticType.Error,
                    MessageData = "Story mode native editor integration failed: " + ex });
                return new ILPostProcessResult(null, diagnostics);
            }
        }
    }

    public static class StoryEditorWeaver
    {
        private const string HookType = "MDPro3.Plugins.Features.StoryMode.StoryDeckEditorHooks";
        public static bool Apply(ModuleDefinition module)
        {
            var hooks = module.GetType(HookType);
            if (hooks == null) return false; // Plugin removed / disabled at build time.
            var marker = Method(hooks, "Installed", 0);
            if (marker.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldc_I4_1)) return false;
            InstallStoryModel(module);
            var view = module.GetType("MDPro3.UI.DeckView");
            var ui = module.GetType("MDPro3.UI.ServantUI.DeckEditorUI");
            var editor = module.GetType("MDPro3.Servant.DeckEditor");

            // bool native methods: use story implementation only for the active story DeckView.
            Override(Method(view, "CanAddCard", 2), Method(hooks, "UsesView", 1), Method(hooks, "CanAdd", 3));
            Override(Method(view, "Save", 0), Method(hooks, "UsesView", 1), Method(hooks, "SaveView", 1));
            Override(Method(editor, "get_Banlist", 0), Method(hooks, "Active", 0), Method(hooks, "GetBanlist", 0));
            Override(Method(view, "GetCardByData", 1), Method(hooks, "UsesView", 1), Method(hooks, "FindVersion", 2));
            WrapReturn(Method(view, "AddCard", 4), Method(hooks, "StampCard", 2));
            var addCopy = Method(view, "AddCard", 4);
            Prepend(addCopy, new[] { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldarg_1),
                Instruction.Create(OpCodes.Call, Method(hooks, "PrepareCard", 2)), Instruction.Create(OpCodes.Starg, addCopy.Parameters[0]) });
            WrapReturn(Method(view, "FromObjectDeckToCodedDeck", 0), Method(hooks, "ExportRarities", 2));
            Prepend(Method(module.GetType("MDPro3.UI.SelectionButton_CardInDeck"), "ShowThisCard", 0),
                new[] { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Call, Method(hooks, "SelectDeckVersion", 1)) });
            var widgetData = Method(module.GetType("MDPro3.UI.UIWidgetCardBase"), "SetCardData", 1);
            AfterVoid(widgetData, Method(hooks, "ConfigureCardWidget", 1));
            Prepend(widgetData, new[] { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldarg_1),
                Instruction.Create(OpCodes.Call, Method(hooks, "PrepareWidgetCard", 2)), Instruction.Create(OpCodes.Starg, widgetData.Parameters[0]) });
            var setCard = module.GetType("MDPro3.UI.CardRawImageHandler").Methods.Single(m => m.Name == "SetCard"
                && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "MDPro3.Duel.YGOSharp.Card");
            AfterVoid(setCard, Method(hooks, "StyleCard", 1));
            var material = Method(module.GetType("MDPro3.MaterialLoader"), "GetCardMaterial", 2);
            var rarityCalls = material.Body.Instructions.Where(i => i.Operand is MethodReference m
                && m.DeclaringType.FullName == "MDPro3.CardRarity" && m.Name == "GetRarity").ToArray();
            if (rarityCalls.Length != 1) throw new InvalidOperationException("Unsupported card material rarity lookup.");
            rarityCalls[0].Operand = Method(module.GetType("MDPro3.Plugins.Features.StoryMode.StoryCardFinish"), "ResolveMaterialRarity", 1);
            var cardsManager = module.GetType("MDPro3.Duel.YGOSharp.CardsManager");
            foreach (var method in AllMethods(cardsManager).Where(m => m.HasBody))
                foreach (var call in method.Body.Instructions.Where(i => i.Operand is MethodReference m
                    && m.DeclaringType.FullName == "MDPro3.CardRarity" && m.Name == "GetRarity"))
                    call.Operand = Method(hooks, method.Name == "MatchCardFilters" ? "SearchRarity" : "SortRarity", 1);
            var match = Method(cardsManager, "MatchCardFilters", 4);
            Prepend(match, new[] { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldarg_1),
                Instruction.Create(OpCodes.Call, Method(hooks, "MatchesRarity", 2)),
                Instruction.Create(OpCodes.Brtrue, match.Body.Instructions[0]), Instruction.Create(OpCodes.Ldc_I4_0), Instruction.Create(OpCodes.Ret) });
            Prepend(Method(module.GetType("MDPro3.UI.Popup.PopupSearchFilter"), "Start", 0),
                new[] { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Call, Method(hooks, "ConfigureRarityFilter", 1)) });

            // Void actions return true from their hook when handled by the story session.
            Handle(Method(ui, "OnSave", 0), Method(hooks, "SaveUI", 1));
            Handle(Method(editor, "OnReturn", 0), Method(hooks, "Return", 1));
            Handle(Method(ui, "OnSubMenu", 0), Method(hooks, "SubMenu", 1));
            Handle(Method(ui, "OnRegulation", 0), Method(hooks, "Regulation", 1));
            Handle(Method(ui, "ShiftToAppearance", 0), Method(hooks, "BlockAppearance", 1));
            Handle(Method(ui, "ChangeRarity", 1), Method(hooks, "ChangeRarity", 2));
            Guard(Method(view, "ImportCardLists", 1), Method(hooks, "AllowImport", 2));
            Guard(Method(ui, "TryStartHandTest", 1), Method(hooks, "AllowHandTest", 1));

            var tcpTick = Method(module.GetType("MDPro3.TcpHelper"), "PerFrameFunction", 0);
            var disconnectShifts = tcpTick.Body.Instructions.Where(i => i.Operand is MethodReference m
                && m.DeclaringType.FullName == "MDPro3.Program" && m.Name == "ShiftToServant").ToArray();
            if (disconnectShifts.Length != 1) throw new InvalidOperationException("Unsupported disconnect return path.");
            disconnectShifts[0].OpCode = OpCodes.Call;
            disconnectShifts[0].Operand = Method(hooks, "ShiftAfterDisconnect", 2);

            var roomShow = Method(module.GetType("MDPro3.Servant.RoomServant"), "ApplyShowArrangement", 1);
            var chatShows = roomShow.Body.Instructions.Where(i => i.Operand is MethodReference m
                && m.DeclaringType.FullName == "MDPro3.UI.ChatPanel" && m.Name == "Show"
                && m.Parameters.Count == 1).ToArray();
            if (chatShows.Length != 1) throw new InvalidOperationException("Unsupported room chat entry point.");
            chatShows[0].OpCode = OpCodes.Call;
            chatShows[0].Operand = Method(hooks, "ShowRoomChat", 2);

            // Every search, bookmark, history and related-card path prints through this method.
            var print = Method(module.GetType("MDPro3.UI.CardCollectionView"), "PrintCards", 1);
            Prepend(print, new[] { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldarg_1),
                Instruction.Create(OpCodes.Call, Method(hooks, "FilterCards", 2)), Instruction.Create(OpCodes.Starg, print.Parameters[0]) });

            var awake = Method(ui, "Awake", 0);
            foreach (var ret in awake.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray())
            {
                // Make existing branches to the return run the hook too.
                ret.OpCode = OpCodes.Ldarg_0; ret.Operand = null;
                var il = awake.Body.GetILProcessor();
                var call = Instruction.Create(OpCodes.Call, Method(hooks, "Configure", 1));
                il.InsertAfter(ret, call); il.InsertAfter(call, Instruction.Create(OpCodes.Ret));
            }
            marker.Body.Instructions.Clear();
            marker.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
            marker.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            return true;
        }

        private static IEnumerable<MethodDefinition> AllMethods(TypeDefinition type)
        {
            foreach (var method in type.Methods) yield return method;
            foreach (var nested in type.NestedTypes)
                foreach (var method in AllMethods(nested)) yield return method;
        }

        private static void InstallStoryModel(ModuleDefinition module)
        {
            var hooks = module.GetType("MDPro3.Plugins.Features.StoryMode.StoryModelHooks");
            var behavior = module.GetType("WindBot.Game.GameBehavior");
            var packet = Method(behavior, "OnPacket", 1);
            var ai = behavior.Fields.Single(f => f.Name == "_ai" && f.FieldType.FullName == "WindBot.Game.GameAI");
            var hint = behavior.Fields.Single(f => f.Name == "_select_hint" && f.FieldType.MetadataType == MetadataType.Int32);
            var last = behavior.Fields.Single(f => f.Name == "_lastMessage" && f.FieldType.FullName == "YGOSharp.OCGWrapper.Enums.GameMessage");
            // Verify selector fields too: a model action must not inherit a previous fallback's targets.
            var gameAI = module.GetType("WindBot.Game.GameAI");
            foreach (string field in new[] { "m_selector", "m_position", "m_attributes", "m_races", "m_selector_pointer", "m_option",
                "m_yesno", "m_materialSelectorHint", "m_place", "m_announce", "m_number", "m_materialSelector" })
                if (!gameAI.Fields.Any(f => f.Name == field)) throw new InvalidOperationException("Unsupported WindBot selector: " + field);
            Prepend(packet, new[] { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Ldfld, ai), Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Ldflda, hint), Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldflda, last),
                Instruction.Create(OpCodes.Call, Method(hooks, "TryHandle", 5)),
                Instruction.Create(OpCodes.Brfalse, packet.Body.Instructions[0]), Instruction.Create(OpCodes.Ret) });
            var installed = Method(hooks, "Installed", 0);
            installed.Body.Instructions.Clear(); installed.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
            installed.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        }

        private static MethodDefinition Method(TypeDefinition type, string name, int arguments)
        {
            var methods = type?.Methods.Where(m => m.Name == name && m.Parameters.Count == arguments).ToArray();
            if (methods == null || methods.Length != 1 || !methods[0].HasBody)
                throw new InvalidOperationException("Unsupported base game method: " + type?.FullName + "." + name);
            return methods[0];
        }

        private static void Prepend(MethodDefinition method, IEnumerable<Instruction> instructions)
        {
            var first = method.Body.Instructions[0]; var il = method.Body.GetILProcessor();
            foreach (var instruction in instructions) il.InsertBefore(first, instruction);
        }

        private static void WrapReturn(MethodDefinition method, MethodDefinition hook)
        {
            foreach (var ret in method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray())
            {
                ret.OpCode = OpCodes.Ldarg_0; ret.Operand = null;
                var call = Instruction.Create(OpCodes.Call, hook);
                method.Body.GetILProcessor().InsertAfter(ret, call);
                method.Body.GetILProcessor().InsertAfter(call, Instruction.Create(OpCodes.Ret));
            }
        }

        private static void AfterVoid(MethodDefinition method, MethodDefinition hook) => WrapReturn(method, hook);

        private static void Override(MethodDefinition target, MethodDefinition predicate, MethodDefinition replacement)
        {
            var code = new List<Instruction>();
            if (predicate.Parameters.Count == 1) code.Add(Instruction.Create(OpCodes.Ldarg_0));
            code.Add(Instruction.Create(OpCodes.Call, predicate));
            code.Add(Instruction.Create(OpCodes.Brfalse, target.Body.Instructions[0]));
            if (!target.IsStatic) code.Add(Instruction.Create(OpCodes.Ldarg_0));
            foreach (var parameter in target.Parameters) code.Add(Instruction.Create(OpCodes.Ldarg, parameter));
            code.Add(Instruction.Create(OpCodes.Call, replacement)); code.Add(Instruction.Create(OpCodes.Ret));
            Prepend(target, code);
        }

        private static void Handle(MethodDefinition target, MethodDefinition hook)
        {
            var code = new List<Instruction> { Instruction.Create(OpCodes.Ldarg_0) };
            foreach (var parameter in target.Parameters.Take(hook.Parameters.Count - 1)) code.Add(Instruction.Create(OpCodes.Ldarg, parameter));
            code.Add(Instruction.Create(OpCodes.Call, hook));
            code.Add(Instruction.Create(OpCodes.Brfalse, target.Body.Instructions[0]));
            code.Add(Instruction.Create(OpCodes.Ret));
            Prepend(target, code);
        }

        private static void Guard(MethodDefinition target, MethodDefinition hook)
        {
            var code = new List<Instruction> { Instruction.Create(OpCodes.Ldarg_0) };
            foreach (var parameter in target.Parameters.Take(hook.Parameters.Count - 1)) code.Add(Instruction.Create(OpCodes.Ldarg, parameter));
            code.Add(Instruction.Create(OpCodes.Call, hook));
            code.Add(Instruction.Create(OpCodes.Brtrue, target.Body.Instructions[0]));
            code.Add(Instruction.Create(OpCodes.Ldc_I4_0)); code.Add(Instruction.Create(OpCodes.Ret));
            Prepend(target, code);
        }
    }
}
