using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MDPro3.Plugins.Features.RpsVisualFix
{
    /// <summary>
    /// Reads result packets before Program.Update consumes TcpHelper's receive list. The observer
    /// never removes or changes packets; its early execution order only gives the visual feature a
    /// copy of the two hand bytes that the game otherwise discards after showing result text.
    /// </summary>
    [DefaultExecutionOrder(-32000)]
    internal sealed class RpsResultPacketObserver : MonoBehaviour
    {
        private const byte HandResultMessage = 0x05;

        private static readonly FieldInfo ReceivedPacketsField = typeof(TcpHelper).GetField(
            "datas", BindingFlags.Static | BindingFlags.NonPublic);

        private readonly HashSet<byte[]> observedPackets = new HashSet<byte[]>();
        private readonly List<Vector2Int> pendingResults = new List<Vector2Int>();
        private RpsVisualFixFeature owner;
        private List<byte[]> receivedPackets;
        private bool accessErrorReported;

        internal void Bind(RpsVisualFixFeature feature)
        {
            owner = feature;
        }

        private void Update()
        {
            if (owner == null || ReceivedPacketsField == null)
            {
                ReportAccessError();
                return;
            }

            if (receivedPackets == null)
            {
                try
                {
                    receivedPackets = ReceivedPacketsField.GetValue(null) as List<byte[]>;
                }
                catch (System.Exception exception)
                {
                    ReportAccessError(exception.Message);
                    return;
                }

                if (receivedPackets == null)
                {
                    ReportAccessError();
                    return;
                }
            }

            pendingResults.Clear();
            lock (receivedPackets)
            {
                if (receivedPackets.Count == 0)
                {
                    observedPackets.Clear();
                    return;
                }

                for (int i = 0; i < receivedPackets.Count; i++)
                {
                    byte[] packet = receivedPackets[i];
                    if (packet == null || !observedPackets.Add(packet))
                        continue;
                    if (TryDecode(packet, out int myHand, out int opponentHand))
                        pendingResults.Add(new Vector2Int(myHand, opponentHand));
                }
            }

            for (int i = 0; i < pendingResults.Count; i++)
                owner.ShowResult(pendingResults[i].x, pendingResults[i].y);
        }

        internal static bool TryDecode(byte[] packet, out int myHand, out int opponentHand)
        {
            myHand = 0;
            opponentHand = 0;
            if (packet == null || packet.Length < 3 || packet[0] != HandResultMessage)
                return false;

            myHand = packet[1];
            opponentHand = packet[2];
            return myHand >= 1 && myHand <= 3 && opponentHand >= 1 && opponentHand <= 3;
        }

        private void ReportAccessError(string detail = null)
        {
            if (accessErrorReported)
                return;

            accessErrorReported = true;
            PluginLog.Error("rpsVisualFix cannot observe TcpHelper receive packets"
                + (string.IsNullOrEmpty(detail) ? string.Empty : ": " + detail));
        }
    }
}
