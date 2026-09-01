using Microsoft.AspNetCore.Mvc;

namespace staff.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AppVersionController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetVersion()
        {
            return Ok(new
            {
                latestVersion = "1.0.0",
                downloadUrl = "https://staff.poornasreecloud.com/downloads/workpulse.apk",
                forceUpdate = false,
                message = "A new version of WorkPulse is available."
            });
        }
    }
}