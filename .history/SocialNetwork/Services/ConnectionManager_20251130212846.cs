using System.Collections.Concurrent;

namespace SocialNetwork.Services
{
    public class ConnectionManager : IConnectionManager
    {
        private static readonly ConcurrentDictionary<int, HashSet<string>> _connections = new();

        public void AddConnection(int userId, string connectionId)
        {
            var set = _connections.GetOrAdd(userId, _ => new HashSet<string>());
            lock (set)
            {
                set.Add(connectionId);
            }
        }

        public void RemoveConnection(int userId, string connectionId)
        {
            if (_connections.TryGetValue(userId, out var set))
            {
                lock (set)
                {
                    set.Remove(connectionId);
                    if (set.Count == 0)
                    {
                        _connections.TryRemove(userId, out _);
                    }
                }
            }
        }

        public HashSet<string> GetConnections(int userId)
        {
            if (_connections.TryGetValue(userId, out var set))
            {
                lock (set)
                {
                    return new HashSet<string>(set);
                }
            }
            return new HashSet<string>();
        }

        public IEnumerable<int> GetOnlineUsers()
        {
            return _connections.Keys;
        }
    }
}
