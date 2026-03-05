using System.Collections.Concurrent;

namespace SocialNetwork.Services
{
    public interface IConnectionManager
    {
        void AddConnection(int userId, string connectionId);
        void RemoveConnection(int userId, string connectionId);
        HashSet<string> GetConnections(int userId);
        IEnumerable<int> GetOnlineUsers();
    }
}
