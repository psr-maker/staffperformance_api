using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using staff;
using staff.Services;
using staff_work_tracking.Data;


namespace staff_work_tracking.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;
        private NotificationService _notific;

        public AuthController(AppDbContext context, IConfiguration config, NotificationService notificationService)
        {
            _context = context;
            _config = config;
            _notific = notificationService;
        }

        [HttpPost("send-otp")]
        public async Task<IActionResult> SendOtp([FromBody] string email)
        {


            if (string.IsNullOrWhiteSpace(email))
                return BadRequest("Name or Email is required");


            var user = await _context.Users
     .FirstOrDefaultAsync(u => u.Email == email);

            // 1. Email not found
            if (user == null)
                return BadRequest(new
                {
                    message = "This email is not found"
                });

            // 2. Email exists but not active
            if (user.Status != "Active")
                return BadRequest(new
                {
                    message = "Your email is not approved yet, wait for director approval"
                });

            var emailToSend = user.Email;
            var otpData = await _context.otp.FirstOrDefaultAsync(o => o.Email == emailToSend);

            if (otpData != null && otpData.Resend_Count >= 3)
            {
                var cooldownTime = otpData.Created_At.AddMinutes(30);

                if (DateTime.UtcNow < cooldownTime)
                {
                    var remainingTime = (cooldownTime - DateTime.UtcNow).Minutes;

                    return BadRequest(new
                    {
                        message = $"Maximum OTP attempts reached. Try again after {remainingTime} minutes."
                    });
                }
                else
                {
                    otpData.Resend_Count = 0;
                }
            }
            var random = new Random();
            string otp = random.Next(100000, 999999).ToString();
            string otpHash;
            using (var sha = SHA256.Create())
            {
                otpHash = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(otp)));
            }

            if (otpData != null)
            {
                otpData.OTP_Hash = otpHash;
                otpData.Resend_Count += 1;

                otpData.Created_At = DateTime.UtcNow;
                otpData.Expire_At = DateTime.UtcNow.AddMinutes(10);
            }
            else
            {
                otpData = new SendOTP
                {
                    Email = emailToSend,
                    OTP_Hash = otpHash,
                    Resend_Count = 0,
                    Created_At = DateTime.UtcNow,
                    Expire_At = DateTime.UtcNow.AddMinutes(10)
                };
                _context.otp.Add(otpData);
            }

            await _context.SaveChangesAsync();

            var mail = new System.Net.Mail.MailMessage();
            mail.From = new System.Net.Mail.MailAddress("cloud2.poornasree@gmail.com");
            mail.To.Add(emailToSend);
            mail.Subject = "Your OTP Code";
            mail.Body = $"Your OTP is {otp}. Valid for 10 minutes.";

            var smtp = new System.Net.Mail.SmtpClient("smtp.gmail.com", 587);
            smtp.Credentials = new System.Net.NetworkCredential(
                "cloud2.poornasree@gmail.com",
                "wdhq bxuo haqk tfkg"
            );
            smtp.EnableSsl = true;

            await smtp.SendMailAsync(mail);

            return Ok(new
            {
                message = "OTP sent successfully",
                attempts = otpData?.Resend_Count ?? 1
            });

        }



        [HttpPost("verify-otp")]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtp request)
        {

            var user = await _context.Users
       .FirstOrDefaultAsync(u => u.Email == request.EmailorName || u.Name == request.EmailorName);

            if (user == null)
                return BadRequest("User not found");

            var otpRow = await _context.otp.FirstOrDefaultAsync(o => o.Email == request.EmailorName);

            if (otpRow == null)
                return BadRequest("OTP not found");

            if (DateTime.UtcNow > otpRow.Expire_At)
                return BadRequest("OTP expired");



            string hash;
            using (var sha = SHA256.Create())
            {
                hash = Convert.ToBase64String(
                    sha.ComputeHash(Encoding.UTF8.GetBytes(request.Otp))
                );
            }

            if (hash != otpRow.OTP_Hash)
                return BadRequest($"Invalid OTP. Attempt {otpRow.Resend_Count} of 3.");

            var Users = await _context.Users.FirstAsync(u => u.Email == request.EmailorName);
            var tokenHandler = new JwtSecurityTokenHandler();
            var keyBytes = Encoding.UTF8.GetBytes(_config["Jwt:Key"]);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
        new Claim("UserId", user.UserId.ToString()),
        new Claim("Role", user.Role),

    }),
                Expires = DateTime.UtcNow.AddYears(1),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(keyBytes),
                    SecurityAlgorithms.HmacSha256Signature
                )
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            var tokenString = tokenHandler.WriteToken(token);


            var audit = new Auditlog
            {
                EntityId = user.UserId.ToString(),
                EntityType = "User",
                Action = "Login",
                Fieldchanged = "LoginStatus",
                Oldvalue = "Not Logged In",
                Newvalue = "Logged In",
                EditedUid = user.UserId.ToString(),
                EditedRole = user.Role,
                ChangeDateandTime = DateTime.UtcNow
            };

            _context.Auditlog.Add(audit);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                token = tokenHandler.WriteToken(token),
                role = user.Role,
                userId = user.UserId
            });



        }


        [Authorize]
        [HttpPost("create-user")]
        public async Task<IActionResult> CreateUser([FromBody] CreateUser dto)
        {
            if (
                string.IsNullOrWhiteSpace(dto.Email) ||
                string.IsNullOrWhiteSpace(dto.Role))
            {
                return BadRequest(" Email, and Role are required");
            }

            var creatorIdClaim = User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            if (creatorIdClaim == null)
                return Unauthorized("Invalid token");

            int creatorId = int.Parse(creatorIdClaim);
            var creatorRole = User.Claims.FirstOrDefault(c => c.Type == "Role")?.Value;
            if (creatorRole == null)
                return Unauthorized("Invalid token");



            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == dto.Email);

            if (existingUser != null)
                return BadRequest("User with this email already exists");

            var user = new User
            {
                Name = dto.Name,
                Email = dto.Email,
                Department = dto.Department,
                Role = dto.Role,
                Created_by = creatorId + "-" + creatorRole,
                Status = "Pending"
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var creator = await _context.Users
    .FirstOrDefaultAsync(u => u.UserId == creatorId);

            if (creator == null)
                return BadRequest("Creator not found");


            await _notific.SendUserCrudNotificationToDirector(
     "Created",
     creator.UserId,
     creator.Name,
     creator.Role,
     creator.Department,
     user.Name
 );
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "User created successfully",
                userId = user.UserId
            });
        }


        [HttpGet("check-email-role")]
        public async Task<IActionResult> CheckEmailRole(string email)
        {
            var user = await _context.Users
                .Where(u => u.Email == email)
                .Select(u => new
                {
                    u.Role,
                    RoleName = _context.Roles
                          .Where(r => r.Id.ToString() == u.Role)
                        .Select(r => r.RoleName)
                        .FirstOrDefault()
                })
                .FirstOrDefaultAsync();

            if (user == null)
            {
                return Ok(new
                {
                    exists = false
                });
            }

            return Ok(new
            {
                exists = true,
               
                role = user.RoleName
            });
        }

        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            var roleClaim = User.Claims.FirstOrDefault(c => c.Type == "Role")?.Value;

            if (userIdClaim == null || roleClaim == null)
                return Unauthorized("Invalid token");

            var audit = new Auditlog
            {
                EntityId = userIdClaim,
                EntityType = "User",
                Action = "Logout",
                Fieldchanged = "LoginStatus",
                Oldvalue = "Logged In",
                Newvalue = "Logged Out",
                EditedUid = userIdClaim,
                EditedRole = roleClaim,
                ChangeDateandTime = DateTime.UtcNow
            };

            _context.Auditlog.Add(audit);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Logged out successfully" });
        }

        [Authorize]
        [HttpPost("register-fcm-token")]
        public async Task<IActionResult> RegisterFcmToken(
    [FromBody] RegisterFcmTokenRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.FcmToken))
                return BadRequest("FCM token is required.");

            var userIdClaim = User.Claims
                .FirstOrDefault(c => c.Type == "UserId")?.Value;

            if (userIdClaim == null)
                return Unauthorized("Invalid token.");

            int loggedInUserId = int.Parse(userIdClaim);

            // Don't trust UserId sent from Flutter.
            // Use the UserId from the JWT.
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == loggedInUserId);

            if (user == null)
                return NotFound("User not found.");

            user.FcmToken = request.FcmToken;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "FCM token saved successfully."
            });
        }


        [Authorize]
        [HttpGet("my-profile")]
        public async Task<IActionResult> GetMyProfile()
        {
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;

            if (string.IsNullOrEmpty(userIdClaim))
                return Unauthorized("Invalid token");

            int userId = int.Parse(userIdClaim);

            var profile = await _context.UserProfile
                .FirstOrDefaultAsync(x => x.UserId == userId);

            if (profile == null)
                return Ok(new { }); 

            return Ok(profile);
        }

        [Authorize]
        [HttpPost("update-profile")]
        [RequestSizeLimit(50_000_000)]
        public async Task<IActionResult> UpdateProfile([FromForm] UpdateUserProfileDto dto)
        {
            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;

            if (string.IsNullOrEmpty(userIdClaim))
                return Unauthorized("Invalid token");

            int userId = int.Parse(userIdClaim);

            var profile = await _context.UserProfile
                .FirstOrDefaultAsync(x => x.UserId == userId);

            if (profile == null)
            {
                profile = new UserProfile
                {
                    UserId = userId,
                    CreatedAt = DateTime.Now
                };

                _context.UserProfile.Add(profile);
            }
            if (dto.ProfileImage != null)
            {
                var folder = "/var/www/uploads/profile";

                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                var fileName = Guid.NewGuid() + Path.GetExtension(dto.ProfileImage.FileName);
                var path = Path.Combine(folder, fileName);

                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await dto.ProfileImage.CopyToAsync(stream);
                }

                profile.ProfileImage = "/uploads/profile/" + fileName;
            }
            // ✅ Update all fields
            profile.Name = dto.Name;
            profile.Email = dto.Email;
            profile.BloodGroup = dto.BloodGroup;
            profile.DateOfBirth = dto.DateOfBirth;
            profile.Gender = dto.Gender;
            profile.ContactNumber = dto.ContactNumber;
            profile.EmergencyContact = dto.EmergencyContact;
            profile.Address = dto.Address;
            profile.EmployeeId = dto.EmployeeId;
            profile.Designation = dto.Designation;
            profile.Department = dto.Department;
            profile.DateOfJoining = dto.DateOfJoining;
            profile.ReportingManager = dto.ReportingManager;
            profile.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Profile updated successfully" });
        }





    }
}