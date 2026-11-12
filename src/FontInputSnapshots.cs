using System;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using ProbablyFonts.Core;
using TMPro;

namespace ProbablyFonts
{
    /// <summary>Keeps the font selector on the input TMP is parsing, including temporary measurements.</summary>
    internal static class FontInputSnapshots
    {
        private static ConditionalWeakTable<TMP_Text, InputState> states = new ConditionalWeakTable<TMP_Text, InputState>();
        private static readonly ConditionalWeakTable<TMP_Text, InputState>.CreateValueCallback createState = CreateState;
        private static bool enabled;

        public static void Start()
        {
            enabled = true;
        }

        public static void Stop()
        {
            enabled = false;
            states = new ConditionalWeakTable<TMP_Text, InputState>();
        }

        public static bool TryGet(TMP_Text text, out string input, out int revision)
        {
            input = null;
            revision = 0;
            if (!enabled || text == null || !states.TryGetValue(text, out InputState state) || !state.Snapshot.HasValue)
                return false;
            input = state.Snapshot.Text;
            revision = state.Snapshot.Revision;
            return true;
        }

        internal static void Capture(TMP_Text text, string source, int start, int length)
        {
            if (!enabled || text == null) return;
            InputState state = states.GetValue(text, createState);
            MarkTypedCapture(state, state.Snapshot.Capture(source, start, length));
        }

        internal static void Capture(TMP_Text text, char[] source, int start, int length)
        {
            if (!enabled || text == null) return;
            InputState state = states.GetValue(text, createState);
            MarkTypedCapture(state, state.Snapshot.Capture(source, start, length));
        }

        internal static void Capture(TMP_Text text, StringBuilder source, int start, int length)
        {
            if (!enabled || text == null) return;
            InputState state = states.GetValue(text, createState);
            MarkTypedCapture(state, state.Snapshot.Capture(source, start, length));
        }

        internal static void BeforeProcessing(TMP_Text text, ref bool backingStringDirty)
        {
            if (!enabled || text == null) return;
            InputState state = states.GetValue(text, createState);
            if (state.TypedCapturePending)
            {
                state.TypedCapturePending = false;
                return;
            }
            if (state.Snapshot.HasValue && !backingStringDirty && !state.NeedsBackingSnapshot) return;

            bool originalDirty = backingStringDirty;
            try
            {
                if (state.ReadBacking == null)
                    state.ReadBacking = ModReflect.Method<Func<string>>(text, "InternalTextBackingArrayToString");
                state.Snapshot.Set(state.ReadBacking());
                state.NeedsBackingSnapshot = false;
            }
            finally
            {
                // The native helper clears this flag, which must remain the game's responsibility.
                backingStringDirty = originalDirty;
            }
        }

        private static void MarkTypedCapture(InputState state, bool captured)
        {
            state.TypedCapturePending = captured;
            state.NeedsBackingSnapshot = !captured;
        }

        private static InputState CreateState(TMP_Text text)
        {
            return new InputState();
        }

        private sealed class InputState
        {
            public readonly TextInputSnapshot Snapshot = new TextInputSnapshot();
            public bool TypedCapturePending;
            public bool NeedsBackingSnapshot;
            public Func<string> ReadBacking;
        }
    }

    [HarmonyPatch(typeof(TMP_Text), "PopulateTextBackingArray", new Type[] { typeof(string), typeof(int), typeof(int) })]
    internal static class StringBackingSnapshotPatch
    {
        private static void Postfix(TMP_Text __instance, string __0, int __1, int __2)
        {
            FontInputSnapshots.Capture(__instance, __0, __1, __2);
        }
    }

    [HarmonyPatch(typeof(TMP_Text), "PopulateTextBackingArray", new Type[] { typeof(char[]), typeof(int), typeof(int) })]
    internal static class ArrayBackingSnapshotPatch
    {
        private static void Postfix(TMP_Text __instance, char[] __0, int __1, int __2)
        {
            FontInputSnapshots.Capture(__instance, __0, __1, __2);
        }
    }

    [HarmonyPatch(typeof(TMP_Text), "PopulateTextBackingArray", new Type[] { typeof(StringBuilder), typeof(int), typeof(int) })]
    internal static class BuilderBackingSnapshotPatch
    {
        private static void Postfix(TMP_Text __instance, StringBuilder __0, int __1, int __2)
        {
            FontInputSnapshots.Capture(__instance, __0, __1, __2);
        }
    }

    [HarmonyPatch(typeof(TMP_Text), "PopulateTextProcessingArray")]
    internal static class ProcessingSnapshotPatch
    {
        private static void Prefix(TMP_Text __instance, ref bool ___m_IsTextBackingStringDirty)
        {
            FontInputSnapshots.BeforeProcessing(__instance, ref ___m_IsTextBackingStringDirty);
        }
    }
}
