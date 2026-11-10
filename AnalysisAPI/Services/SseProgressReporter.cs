using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CapstoneServices.Galaxy;

namespace AnalysisAPI.Services
{
    /// <summary>
    /// Progress reporter that stores progress updates for SSE streaming
    /// </summary>
    public sealed class SseProgressReporter : IProgressReporter
    {
        private readonly ConcurrentQueue<ProgressUpdate> _updates = new();
        private readonly SemaphoreSlim _signal = new(0);
        private bool _completed = false;
        
        public void ReportProgress(string step, string message, int? current = null, int? total = null, object? details = null)
        {
            var update = new ProgressUpdate
            {
                Timestamp = DateTimeOffset.UtcNow,
                Step = step,
                Message = message,
                Current = current,
                Total = total,
                Details = details,
                Percentage = current.HasValue && total.HasValue && total.Value > 0 
                    ? (int)Math.Round((double)current.Value / total.Value * 100) 
                    : null
            };
            
            _updates.Enqueue(update);
            _signal.Release();
        }
        
        public void Complete()
        {
            _completed = true;
            _signal.Release();
        }
        
        public async IAsyncEnumerable<ProgressUpdate> GetUpdatesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Yield any existing updates immediately
            while (_updates.TryDequeue(out var update))
            {
                yield return update;
            }
            
            // Then wait for new updates
            while (!_completed || !_updates.IsEmpty)
            {
                try
                {
                    await _signal.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Client disconnected
                    yield break;
                }
                
                while (_updates.TryDequeue(out var nextUpdate))
                {
                    yield return nextUpdate;
                }
                
                if (_completed && _updates.IsEmpty)
                    break;
            }
        }
    }
    
    public sealed class ProgressUpdate
    {
        public DateTimeOffset Timestamp { get; set; }
        public string Step { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int? Current { get; set; }
        public int? Total { get; set; }
        public int? Percentage { get; set; }
        public object? Details { get; set; }
    }
}
