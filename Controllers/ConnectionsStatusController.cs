using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using staff_work_tracking.Data;

namespace staff.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ConnectionsStatusController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;
        public ConnectionsStatusController(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _config = configuration;
        }
    

        [HttpGet("system_status")]
        public async Task<IActionResult> SystemStatus()
        {
            /* ================= DATABASE ================= */
            var db = _context.Database;
            bool dbConnected = await db.CanConnectAsync();
            DbConnection conn = db.GetDbConnection();

            var database = new
            {
                connected = dbConnected,
                provider = db.ProviderName,
                database = conn.Database,
                dataSource = conn.DataSource
            };

            ///* ================= EMAIL ================= */
            //var emailSection = _config.GetSection("EmailSettings");

            //bool emailConfigured =
            //    emailSection.Exists() &&
            //    !string.IsNullOrEmpty(emailSection["Host"]) &&
            //    !string.IsNullOrEmpty(emailSection["From"]);

            //var email = new
            //{
            //    configured = emailConfigured,
            //    host = emailSection["Host"],
            //    port = emailSection["Port"],
            //    enableSSL = emailSection["EnableSSL"],
            //    from = emailSection["From"]
            //};

            ///* ================= JWT ================= */
            //var jwtSection = _config.GetSection("Jwt");

            //bool jwtConfigured = !string.IsNullOrEmpty(jwtSection["Key"]);

            //var jwt = new
            //{
            //    configured = jwtConfigured,
            //    issuer = jwtSection["Issuer"],         
            //    audience = jwtSection["Audience"],      
            //    expiresInMinutes = jwtSection["ExpiresInMinutes"]
            //};

            return Ok(new
            {
                status = "System running",
                database,
                //email,
                //jwt
            });
        }
    }
}
