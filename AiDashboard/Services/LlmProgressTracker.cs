using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AiDashboard.Services
{
    /// <summary>
    /// Helper class for managing LLM request timeouts and progress tracking.
    /// Handles the 5-minute max wait and 2-minute pause detection.
    /// </summary>
    public class LlmProgressTracker : IDisposable
    {
        private readonly Stopwatch _stopwatch = new();
        private DateTime _lastProgressUpdate = DateTime.UtcNow;
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _disposed;
        
        // Timeout settings based on requirements
        public const int MaxTotalTimeoutMs = 300000; // 5 minutes total max
        public const int PauseTimeoutMs = 40000;     // 40 seconds pause detection
        public const int ShortTimeoutMs = 10000;      // 10 seconds for unit tests
        
        /// <summary>
        /// Current elapsed time
        /// </summary>
        public TimeSpan ElapsedTime => _stopwatch.Elapsed;
        
        /// <summary>
        /// Time since last progress update
        /// </summary>
        public TimeSpan TimeSinceLastUpdate => DateTime.UtcNow - _lastProgressUpdate;
        
        /// <summary>
        /// Whether the operation is still active
        /// </summary>
        public bool IsActive { get; private set; }
        
        /// <summary>
        /// Current progress percentage (0-100)
        /// </summary>
        public double ProgressPercentage { get; private set; }
        
        /// <summary>
        /// Estimated time remaining
        /// </summary>
        public TimeSpan? EstimatedTimeRemaining { get; private set; }
        
        /// <summary>
        /// Event raised when progress is updated
        /// </summary>
        public event EventHandler<ProgressUpdateEventArgs>? ProgressUpdated;
        
        /// <summary>
        /// Event raised when timeout occurs
        /// </summary>
        public event EventHandler<TimeoutEventArgs>? TimeoutOccurred;
        
        /// <summary>
        /// Start tracking progress with specified timeout
        /// </summary>
        /// <param name="timeoutMs">Total timeout in milliseconds (default: 5 minutes)</param>
        /// <param name="pauseTimeoutMs">Pause detection timeout in milliseconds (default: 2 minutes)</param>
        /// <returns>CancellationToken to use for the operation</returns>
        public CancellationToken Start(int timeoutMs = MaxTotalTimeoutMs, int pauseTimeoutMs = PauseTimeoutMs)
        {
            if (IsActive)
            {
                throw new InvalidOperationException("Progress tracker is already active");
            }
            
            _cancellationTokenSource = new CancellationTokenSource();
            _cancellationTokenSource.CancelAfter(timeoutMs);
            
            _stopwatch.Restart();
            _lastProgressUpdate = DateTime.UtcNow;
            IsActive = true;
            ProgressPercentage = 0;
            
            // Start monitoring for pause timeout
            _ = MonitorPauseTimeoutAsync(pauseTimeoutMs, _cancellationTokenSource.Token);
            
            return _cancellationTokenSource.Token;
        }
        
        /// <summary>
        /// Update progress (should be called periodically during LLM processing)
        /// </summary>
        /// <param name="percentage">Progress percentage (0-100)</param>
        public void UpdateProgress(double percentage)
        {
            if (!IsActive) return;
            
            ProgressPercentage = Math.Clamp(percentage, 0, 100);
            _lastProgressUpdate = DateTime.UtcNow;
            
            // Simple linear estimation
            if (ProgressPercentage > 0 && ProgressPercentage < 100)
            {
                var timePerPercent = ElapsedTime.TotalSeconds / ProgressPercentage;
                var remainingPercent = 100 - ProgressPercentage;
                EstimatedTimeRemaining = TimeSpan.FromSeconds(timePerPercent * remainingPercent);
            }
            else
            {
                EstimatedTimeRemaining = null;
            }
            
            ProgressUpdated?.Invoke(this, new ProgressUpdateEventArgs
            {
                ProgressPercentage = ProgressPercentage,
                ElapsedTime = ElapsedTime,
                EstimatedTimeRemaining = EstimatedTimeRemaining
            });
        }
        
        /// <summary>
        /// Signal that progress was made (for operations without percentage tracking)
        /// </summary>
        public void SignalProgress()
        {
            _lastProgressUpdate = DateTime.UtcNow;
        }
        
        /// <summary>
        /// Complete the operation successfully
        /// </summary>
        public void Complete()
        {
            if (!IsActive) return;
            
            ProgressPercentage = 100;
            IsActive = false;
            _stopwatch.Stop();
            
            ProgressUpdated?.Invoke(this, new ProgressUpdateEventArgs
            {
                ProgressPercentage = 100,
                ElapsedTime = ElapsedTime,
                EstimatedTimeRemaining = TimeSpan.Zero
            });
        }
        
        /// <summary>
        /// Cancel the operation
        /// </summary>
        public void Cancel()
        {
            _cancellationTokenSource?.Cancel();
            IsActive = false;
            _stopwatch.Stop();
        }
        
        /// <summary>
        /// Monitor for pause timeout (2 minutes of no progress)
        /// </summary>
        private async Task MonitorPauseTimeoutAsync(int pauseTimeoutMs, CancellationToken cancellationToken)
        {
            try
            {
                while (IsActive && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, cancellationToken); // Check every second
                    
                    if (TimeSinceLastUpdate.TotalMilliseconds > pauseTimeoutMs)
                    {
                        TimeoutOccurred?.Invoke(this, new TimeoutEventArgs
                        {
                            TimeoutType = TimeoutType.PauseDetected,
                            TimeSinceLastUpdate = TimeSinceLastUpdate,
                            TotalElapsedTime = ElapsedTime
                        });
                        
                        Cancel();
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            
            Cancel();
            _cancellationTokenSource?.Dispose();
            _disposed = true;
        }
    }
    
    public class ProgressUpdateEventArgs : EventArgs
    {
        public double ProgressPercentage { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public TimeSpan? EstimatedTimeRemaining { get; set; }
    }
    
    public class TimeoutEventArgs : EventArgs
    {
        public TimeoutType TimeoutType { get; set; }
        public TimeSpan TimeSinceLastUpdate { get; set; }
        public TimeSpan TotalElapsedTime { get; set; }
    }
    
    public enum TimeoutType
    {
        /// <summary>
        /// Total timeout exceeded (5 minutes)
        /// </summary>
        TotalTimeout,
        
        /// <summary>
        /// Pause detected (2 minutes of no progress)
        /// </summary>
        PauseDetected
    }
}
