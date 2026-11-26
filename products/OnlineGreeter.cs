using System.Collections.Generic;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("OnlineGreeter", "Paradox Studios", "1.3.0")]
    [Description("Welcomes players to the server globally and privately with a custom chat icon and /online command.")]
    public class OnlineGreeter : RustPlugin
    {
        // Replace the zeros below with a 17-digit SteamID64 to use its avatar as your server's chat icon.
        // Leave as "0" for the default Rust icon.
        private const string ChatIconSteamId = "76561198027613962";

        // Tracks players who have already been greeted during their current session
        private readonly HashSet<ulong> _greetedPlayers = new HashSet<ulong>();

        private void OnServerInitialized()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player != null)
                {
                    _greetedPlayers.Add(player.userID);
                }
            }
        }

        private void OnPlayerSleepEnded(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return;

            if (_greetedPlayers.Contains(player.userID)) return;

            _greetedPlayers.Add(player.userID);

            int onlineCount = BasePlayer.activePlayerList.Count;
            int maxPlayers = ConVar.Server.maxplayers;

            // Global welcome broadcast to everyone online
            string globalMessage = $"Welcome to <color=#ff4444>Black Drakes Rusty PlayGround</color> <color=#55aaff>{player.displayName}</color>. We hope that you enjoy our server! " +
                                   $"(<color=#ffcc00>{onlineCount}/{maxPlayers}</color> online)";

            BroadcastMessage(globalMessage);

            // Personal welcome message sent directly to the joining player
            string personalMessage = $"<color=#66ff66>Welcome back, {player.displayName}!</color> " +
                                     $"Type <color=#ffcc00>/online</color> anytime to see who is playing.";

            SendPrivateMessage(player, personalMessage);
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;

            _greetedPlayers.Remove(player.userID);

            int onlineCount = BasePlayer.activePlayerList.Count - 1;
            if (onlineCount < 0) onlineCount = 0;

            int maxPlayers = ConVar.Server.maxplayers;

            string message = $"<color=#ff6666>{player.displayName}</color> has left the server. " +
                             $"(<color=#ffcc00>{onlineCount}/{maxPlayers}</color> online)";

            BroadcastMessage(message);
        }

        [ChatCommand("online")]
        private void CmdOnline(BasePlayer player, string command, string[] args)
        {
            var activePlayers = BasePlayer.activePlayerList;
            int onlineCount = activePlayers.Count;
            int maxPlayers = ConVar.Server.maxplayers;

            List<string> playerNames = activePlayers.Select(p => p.displayName).ToList();
            string namesList = playerNames.Count > 0 ? string.Join(", ", playerNames) : "None";

            string message = $"<color=#55aaff>Players Online ({onlineCount}/{maxPlayers}):</color>\n" +
                             $"<color=#ffffff>{namesList}</color>";

            SendPrivateMessage(player, message);
        }

        private void BroadcastMessage(string message)
        {
            ulong iconId = 0;
            ulong.TryParse(ChatIconSteamId, out iconId);

            foreach (var activePlayer in BasePlayer.activePlayerList)
            {
                if (activePlayer != null && activePlayer.IsConnected)
                {
                    activePlayer.SendConsoleCommand("chat.add", 2, iconId, message);
                }
            }
        }

        private void SendPrivateMessage(BasePlayer player, string message)
        {
            if (player == null || !player.IsConnected) return;

            ulong iconId = 0;
            ulong.TryParse(ChatIconSteamId, out iconId);

            player.SendConsoleCommand("chat.add", 2, iconId, message);
        }
    }
}
