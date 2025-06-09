using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NexusDump
{
    /// <summary>
    /// Rate limiting tracker for NexusMods API to prevent hitting API limits
    /// </summary>
    public class ApiRateLimitTracker
    {
        private int? _dailyRemaining;
        private int? _hourlyRemaining;
        private DateTime? _dailyReset;
        private DateTime? _hourlyReset;
        private DateTime _lastCall = DateTime.MinValue;
        private readonly object _lock = new object();

        /// <summary>
        /// Updates rate limit information from API response headers
        /// </summary>
        /// <param name="response">HTTP response from NexusMods API</param>
        public void UpdateFromResponse(HttpResponseMessage response)
        {
            lock (_lock)
            {
                _lastCall = DateTime.UtcNow;

                // Extract rate limit headers - NexusMods uses these header names
                if (response.Headers.TryGetValues("x-rl-daily-remaining", out var dailyRemainingValues))
                {
                    if (int.TryParse(dailyRemainingValues.FirstOrDefault(), out var dailyRemaining))
                    {
                        _dailyRemaining = dailyRemaining;
                    }
                }

                if (response.Headers.TryGetValues("x-rl-hourly-remaining", out var hourlyRemainingValues))
                {
                    if (int.TryParse(hourlyRemainingValues.FirstOrDefault(), out var hourlyRemaining))
                    {
                        _hourlyRemaining = hourlyRemaining;
                    }
                }

                if (response.Headers.TryGetValues("x-rl-daily-reset", out var dailyResetValues))
                {
                    if (long.TryParse(dailyResetValues.FirstOrDefault(), out var dailyResetTimestamp))
                    {
                        _dailyReset = DateTimeOffset.FromUnixTimeSeconds(dailyResetTimestamp).DateTime;
                    }
                }

                if (response.Headers.TryGetValues("x-rl-hourly-reset", out var hourlyResetValues))
                {
                    if (long.TryParse(hourlyResetValues.FirstOrDefault(), out var hourlyResetTimestamp))
                    {
                        _hourlyReset = DateTimeOffset.FromUnixTimeSeconds(hourlyResetTimestamp).DateTime;
                    }
                }

                LogRateLimitStatus();
            }
        }

        /// <summary>
        /// Waits if needed to respect rate limits before making API calls
        /// </summary>
        public async Task WaitIfNeeded()
        {
            lock (_lock)
            {
                // Always wait at least the configured delay between calls
                var timeSinceLastCall = DateTime.UtcNow - _lastCall;
                var minDelay = TimeSpan.FromMilliseconds(Program.config.RateLimitDelayMs);

                if (timeSinceLastCall < minDelay)
                {
                    var waitTime = minDelay - timeSinceLastCall;
                    ColoredLogger.LogInfo($"Rate limiting: waiting {waitTime.TotalMilliseconds:F0}ms...");
                    Thread.Sleep(waitTime);
                }

                // Check if we need to wait due to low API call limits
                var needsWait = false;
                var waitMessage = "";

                if (_hourlyRemaining.HasValue && _hourlyRemaining.Value <= Program.config.MinHourlyCallsRemaining)
                {
                    needsWait = true;
                    var resetTime = _hourlyReset ?? DateTime.UtcNow.AddHours(1);
                    var waitTime = resetTime - DateTime.UtcNow;
                    waitMessage = $"Hourly API limit low ({_hourlyRemaining} remaining). Waiting until {resetTime:HH:mm:ss UTC} ({waitTime.TotalMinutes:F1} minutes)";
                }
                else if (_dailyRemaining.HasValue && _dailyRemaining.Value <= Program.config.MinDailyCallsRemaining)
                {
                    needsWait = true;
                    var resetTime = _dailyReset ?? DateTime.UtcNow.AddDays(1);
                    var waitTime = resetTime - DateTime.UtcNow;
                    waitMessage = $"Daily API limit low ({_dailyRemaining} remaining). Waiting until {resetTime:yyyy-MM-dd HH:mm:ss UTC} ({waitTime.TotalHours:F1} hours)";
                }

                if (needsWait)
                {
                    ColoredLogger.LogWarning("API Rate Limit Warning");
                    ColoredLogger.LogWarning(waitMessage);
                    ColoredLogger.LogInfo($"You can adjust MinHourlyCallsRemaining ({Program.config.MinHourlyCallsRemaining}) and MinDailyCallsRemaining ({Program.config.MinDailyCallsRemaining}) in config.json");
                    ColoredLogger.LogInfo("Press Ctrl+C to stop or wait for automatic resume...");
                }
            }

            if (CheckNeedsLongWait())
            {
                await WaitForReset();
            }
        }

        /// <summary>
        /// Checks if a long wait is needed due to low API limits
        /// </summary>
        /// <returns>True if a long wait is needed</returns>
        private bool CheckNeedsLongWait()
        {
            lock (_lock)
            {
                if (_hourlyRemaining.HasValue && _hourlyRemaining.Value <= Program.config.MinHourlyCallsRemaining)
                    return true;
                if (_dailyRemaining.HasValue && _dailyRemaining.Value <= Program.config.MinDailyCallsRemaining)
                    return true;
                return false;
            }
        }

        /// <summary>
        /// Waits for the rate limit to reset
        /// </summary>
        private async Task WaitForReset()
        {
            DateTime waitUntil;
            string waitType;

            lock (_lock)
            {
                if (_hourlyRemaining.HasValue && _hourlyRemaining.Value <= Program.config.MinHourlyCallsRemaining)
                {
                    waitUntil = _hourlyReset ?? DateTime.UtcNow.AddHours(1);
                    waitType = "hourly";
                }
                else
                {
                    waitUntil = _dailyReset ?? DateTime.UtcNow.AddDays(1);
                    waitType = "daily";
                }
            }

            while (DateTime.UtcNow < waitUntil)
            {
                var remaining = waitUntil - DateTime.UtcNow;
                ColoredLogger.LogInfo($"Waiting for {waitType} reset... {remaining.TotalMinutes:F1} minutes remaining");

                // Wait in smaller chunks so we can show progress
                var waitTime = remaining.TotalMinutes > 5 ? TimeSpan.FromMinutes(5) : remaining;
                await Task.Delay(waitTime);
            }

            ColoredLogger.LogSuccess($"{waitType} rate limit reset! Resuming operations...");
        }

        /// <summary>
        /// Logs the current rate limit status
        /// </summary>
        private void LogRateLimitStatus()
        {
            var status = new List<string>();

            if (_hourlyRemaining.HasValue)
                status.Add($"Hourly: {_hourlyRemaining} remaining");
            if (_dailyRemaining.HasValue)
                status.Add($"Daily: {_dailyRemaining} remaining");

            if (status.Any())
            {
                ColoredLogger.LogInfo($"API Limits - {string.Join(", ", status)}");
            }
        }
    }
}
