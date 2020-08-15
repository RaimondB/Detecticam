﻿using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DetectiCam.Core.Pipeline
{
    public class PeriodicTrigger : IDisposable
    {
        private readonly ILogger _logger;
        private readonly List<ITimestampTrigger> _subjects;
        private Timer? _timer;
        private bool disposedValue;

        public PeriodicTrigger(ILogger logger, IEnumerable<ITimestampTrigger> subjects)
        {
            _logger = logger;
            _subjects = new List<ITimestampTrigger>(subjects);
        }

        public void Start(TimeSpan initialDelay, TimeSpan interval)
        {
            int triggerId = 0;
            // Set up a timer object that will trigger the frame-grab at a regular interval.
            _timer = new Timer(s /* state */ =>
            {
                var now = DateTime.Now;
                triggerId++;
                _logger.LogInformation("Timer triggered at:{triggerTimestamp} wit id:{triggerId}", now, triggerId);
                foreach(var subject in _subjects)
                {
                    subject.SetNextTrigger(now, triggerId);
                }
            }, null, initialDelay, interval);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _timer?.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}