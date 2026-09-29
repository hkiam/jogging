using System;

namespace Jogging.UI
{
    /// <summary>
    /// Lightweight global channel for transient on-screen messages (overtakes, milestones,
    /// emotes, finish). Any system posts; the RaceHud shows them as popups.
    /// </summary>
    public static class RaceMessages
    {
        public static event Action<string> OnMessage;
        public static void Post(string message) => OnMessage?.Invoke(message);
    }
}
