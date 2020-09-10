using DetectiCam.Core.Common;
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
            var urlTemplate = frame.Metadata.Info.CallbackUrl;
            if (urlTemplate != null)
            {
                _logger.LogInformation("Webhook notify for {streamId}", frame.Metadata.Info.Id);

                var replacedUrl = TokenReplacer.ReplaceTokens(urlTemplate, frame);
                try
                {
                    var uri = new Uri(replacedUrl);

                    using var client = _clientFactory.CreateClient();
                    await client.GetAsync(uri).ConfigureAwait(false);
                }
                catch(UriFormatException ex)
                {
                    _logger.LogError(ex, "Invalid CallbackUrl for {streamId}:{callbackUrl}", 
                        frame.Metadata.Info.Id, replacedUrl);
                }
                catch(HttpRequestException hre)
                {
                    _logger.LogError(hre, "Error in http call for {streamId}:{callbackUrl}",
                        frame.Metadata.Info.Id, replacedUrl);
                }
            }
        }

        public Task StopProcessingAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
