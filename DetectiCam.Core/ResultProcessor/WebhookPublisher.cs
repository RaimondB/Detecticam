using DetectiCam.Core.VideoCapturing;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DetectiCam.Core.ResultProcessor
{
    public class WebhookPublisher : IAsyncSingleResultProcessor
    {
        private readonly ILogger _logger;
        private readonly IHttpClientFactory _clientFactory;

        public WebhookPublisher(ILogger<WebhookPublisher> logger,
                                IHttpClientFactory clientFactory)
        {
            _logger = logger;
            _clientFactory = clientFactory;
        }

        public Task ProcessResultAsync(VideoFrame frame)
        {
            if (frame is null) throw new ArgumentNullException(nameof(frame));

            return ProcessResultInternalAsync(frame);
        }

        private async Task ProcessResultInternalAsync(VideoFrame frame)
        {
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
