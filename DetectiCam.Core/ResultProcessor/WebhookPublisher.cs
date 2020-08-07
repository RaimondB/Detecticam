using DetectiCam.Core.Detection;
using DetectiCam.Core.VideoCapturing;
using DetectiCam.Core.Visualization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DetectiCam.Core.ResultProcessor
{
    public class WebhookPublisher :  IAsyncSingleResultProcessor
    {
        private readonly ILogger _logger;
        private readonly IHttpClientFactory _clientFactory;

        public WebhookPublisher(ILogger<WebhookPublisher> logger,
                                IHttpClientFactory clientFactory)
        {
            _logger = logger;
            _clientFactory = clientFactory;
        }

        public async Task ProcessResultAsync(VideoFrame frame, IList<DnnDetectedObject> results)
        {
            if (frame is null) throw new ArgumentNullException(nameof(frame));
            if (results is null) throw new ArgumentNullException(nameof(results));

            var url = frame.Metadata.Info.CallbackUrl;
            if (url != null)
            {
                _logger.LogInformation("Webhook notify for {streamId}", frame.Metadata.Info.Id);
                using var client = _clientFactory.CreateClient();
                await client.GetAsync(url).ConfigureAwait(false);
            }
        }

        public Task StopProcessingAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
