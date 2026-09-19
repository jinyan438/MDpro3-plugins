namespace MDPro3.Plugins.Features.StoryMode
{
    // STOC_GAME_MSG carries the core message followed by its payload.
    // START supplies the local seat after the first-player choice, not the lobby seat.
    public sealed class StoryDuelPackets
    {
        public bool Started { get; private set; }
        public bool Finished { get; private set; }
        public bool Won { get; private set; }
        private int seat;

        public void Observe(byte[] packet)
        {
            if (Finished || packet == null || packet.Length < 3 || packet[0] != 1) return;
            if (packet[1] == 4 && packet.Length >= 19 && !Started)
            {
                int player = packet[2];
                if ((player & 0xf0) != 0 || (player & 0xf) > 1) return;
                seat = player & 0xf;
                Started = true;
            }
            else if (packet[1] == 5 && packet.Length >= 4 && Started && packet[2] <= 2)
            {
                Won = packet[2] == seat;
                Finished = true;
            }
        }
    }
}
