using DetectiCam.Core.VideoCapturing;
//using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace DetectiCam.Controllers
{
//    [Route("api/[controller]")]
//    [ApiController]
    public class CameraController //: ControllerBase
    {
        private readonly ILogger _logger;
        private readonly VideoStreamsOptions _options;
        private readonly SnapshotOptions _snapshotOptions;

        private readonly MultiStreamBatchedProcessorPipeline _pipeline;

        public CameraController(ILogger<CameraController> logger,
                                MultiStreamBatchedProcessorPipeline pipeline,
                                IOptions<VideoStreamsOptions> options,
                                IOptions<SnapshotOptions> snapshotOptions)
        {
            if (logger is null) throw new ArgumentNullException(nameof(logger));
            if (options is null) throw new ArgumentNullException(nameof(options));
            if (pipeline is null) throw new ArgumentNullException(nameof(pipeline));
            if (snapshotOptions is null) throw new ArgumentNullException(nameof(snapshotOptions));

            _logger = logger;
            _options = options.Value;
            _pipeline = pipeline;
            _snapshotOptions = snapshotOptions.Value;
        }


        //        [HttpGet]
        //        public ActionResult<IEnumerable<string>> Get()
        public IEnumerable<string> Get()
        {
            if (_snapshotOptions.Enabled)
            {
                return _options.Select(o => o.Id).ToList();
            }
            else
            {
                return null; // NotFound();
            }
        }

////        [HttpGet("{streamId}/snapshot")]
//        public IActionResult CreateSnapshot(string streamId)
//        {
//            if (_snapshotOptions.Enabled)
//            {

//                var ms = new MemoryStream();

//                var grabber = _pipeline.GetGrabberForStream(streamId);

//                if (grabber != null)
//                {
//                    grabber.CreateSnapshot(ms);

//                    Response.Headers.Add("Content-Disposition", new ContentDisposition
//                    {
//                        FileName = $"Snapshot-{DateTime.Now:s}.png",
//                        Inline = true // false = prompt the user for downloading; true = browser to try to show the file inline
//                    }.ToString());

//                    ms.Seek(0, SeekOrigin.Begin);
//                    _logger.LogInformation("Snapshot created");

//                    return File(ms, "image/png");
//                }
//                else
//                {
//                    _logger.LogWarning("Snapshot failed");

//                    return NoContent();
//                }
//            }
//            else
//            {
//                return NotFound();
//            }
//        }
    }
}
