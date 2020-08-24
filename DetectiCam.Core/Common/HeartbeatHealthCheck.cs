using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DetectiCam.Core.Common
{
    public class HeartbeatHealthCheck<T> : IHealthCheck, IHeartbeatReporter
    {
        public string Name => "heartbeat_check";

        private readonly TimeSpan _maxHeartbeatTimeout = TimeSpan.FromSeconds(30);
        private DateTimeOffset _lastHeartbeat = DateTimeOffset.Now;
        private readonly object _heartbeatLock = new object(); 

        public void ReportHeartbeat()
        {
            lock(_heartbeatLock)
            {
                _lastHeartbeat = DateTimeOffset.Now;
            }
        }

        private bool IsHeartbeatOK()
        {
            lock (_heartbeatLock)
            {
                return (_lastHeartbeat + _maxHeartbeatTimeout) > DateTimeOffset.Now;
            }
        }

        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {

            if (IsHeartbeatOK())
            {
                return Task.FromResult(
                    HealthCheckResult.Healthy("Heartbeat within range"));
            }
            else
            {
                return Task.FromResult(
                    HealthCheckResult.Unhealthy(
                        $"Heartbeat outside range, last beat at: {_lastHeartbeat.DateTime}"));
            }
        }
    }
}
