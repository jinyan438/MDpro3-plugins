using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MDPro3.Plugins.Features.StoryMode
{
    [DefaultExecutionOrder(-31990)]
    internal sealed class StoryDuelObserver : MonoBehaviour
    {
        private static readonly FieldInfo Packets = typeof(TcpHelper).GetField("datas", BindingFlags.Static | BindingFlags.NonPublic);
        private readonly HashSet<byte[]> seen = new HashSet<byte[]>();
        private readonly List<byte[]> pending = new List<byte[]>();
        internal StoryModeFeature Owner;
        internal static bool Available => Packets != null;

        // Run before Program.OnApplicationQuit persists ordinary game preferences.
        private void OnApplicationQuit() { Owner?.StopModel(); Owner?.RestoreAppearance(); }

        private void Update()
        {
            if (Owner == null || !Owner.IsChallengeConnection || Packets == null) { seen.Clear(); return; }
            var queue = Packets.GetValue(null) as List<byte[]>;
            if (queue == null) return;
            pending.Clear();
            lock (queue)
            {
                seen.RemoveWhere(packet => !queue.Contains(packet));
                foreach (var packet in queue)
                    if (packet != null && seen.Add(packet)) pending.Add(packet);
            }
            // No disk IO, UI calls or packet changes while the receive queue is locked.
            foreach (var packet in pending) Owner.Observe(packet);
        }
    }
}
