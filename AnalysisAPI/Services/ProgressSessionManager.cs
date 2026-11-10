using System.Collections.Concurrent;

namespace AnalysisAPI.Services
{
    /// <summary>
    /// Manages progress reporting sessions for ongoing Galaxy operations
    /// </summary>
    public sealed class ProgressSessionManager
    {
        private readonly ConcurrentDictionary<string, SseProgressReporter> _sessions = new();
        
        public SseProgressReporter CreateSession(string sessionId)
        {
            var reporter = new SseProgressReporter();
            _sessions[sessionId] = reporter;
            return reporter;
        }
        
        public SseProgressReporter? GetSession(string sessionId)
        {
            _sessions.TryGetValue(sessionId, out var reporter);
            return reporter;
        }
        
        public void RemoveSession(string sessionId)
        {
            _sessions.TryRemove(sessionId, out _);
        }
    }
}
