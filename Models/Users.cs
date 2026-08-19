using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace staff            
{

    public class User
    {
        [Key]
        public int UserId { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Department { get; set; }
        public string Role { get; set; }
        public string Created_by { get; set; }
        public string Status { get; set; }
        public bool wasEdited { get; set; }
    }
    public class Department
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string DepartmentName { get; set; }

        public string SubDepartment { get; set; }

        public string Zone { get; set; }
    }
    public class SendOTP
    {
        [Key]
        public int Id { get; set; }
        public string Email { get; set; }
        public string OTP_Hash { get; set; }
        public int Resend_Count { get; set; }
        public DateTime Created_At { get; set; }
        public DateTime Expire_At { get; set; }
    }
    public class Goal
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(10)]
        public string? GoalCode { get; set; }

        [Required]
        public string Title { get; set; }

        public string? Priority { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime DueDate { get; set; }
        public DateTime? Completed_Date { get; set; }

        public string? Status { get; set; }

        public int Progress { get; set; }
        public int Goalpoints { get; set; }

        public string Assign_To { get; set; }

        public string? Assign_By { get; set; }

        public string? Department { get; set; }
    }
    public class TaskTable
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(10)]
        public string TaskCode { get; set; }
        public string GoalCode { get; set; }
        public string Task { get; set; }
        public string Description { get; set; }
        public string Priority { get; set; }

        public string Status { get; set; } = "NotStarted";

        public DateTime Created_At { get; set; }
        public DateTime Due_Date { get; set; }

        public DateTime Completed_Date { get; set; }   

        public int Members { get; set; }
        public bool wasEdited { get; set; }
        public string PerformanceType { get; set; } = "Default";

        public int? Quantity { get; set; }

        public TimeSpan? StartTime { get; set; }

        public TimeSpan? EndTime { get; set; }
    }
    public class TaskMember
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(10)]
        public string TMCode { get; set; } 

        [MaxLength(10)]
        public string TaskCode { get; set; } 

        public string Assign_To { get; set; }
        public string Assign_By { get; set; }
        public string UserStatus { get; set; } = "Not Started";
        public DateTime Assigned_At { get; set; }
    }
    public class Auditlog {

        public int Id { get; set; }
        public string EntityId { get; set; }
        public string EntityType {get;set;}
        public string Action { get; set; }

        public string Fieldchanged { get; set; }
        public string Oldvalue { get; set; }
        public string Newvalue { get; set; }
        public string EditedUid { get; set; }
        public string EditedRole { get; set; }
        public DateTime ChangeDateandTime { get; set; }

    }
    public class TaskReview
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string? TaskCode { get; set; }

        [Required]
        public string? ReviewedById { get; set; }

        [Required]
        public int SystemPoints { get; set; }

        public int FinalPoints { get; set; }

        public bool IsDelayJustified { get; set; } = false;

        public string? DelayReason { get; set; }

        public string? Comment { get; set; }

        public DateTime ReviewedAt { get; set; } = DateTime.Now;
    }
    public class Announcement
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Title { get; set; }

        public string? Description { get; set; }

        [Required]
        public string FileType { get; set; }
        public string? FileName { get; set; }
        public string? FilePath { get; set; }  

        public string? JsonData { get; set; } 

        [Required]
        public string TargetRole { get; set; } 

        public string CreatedBy { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }
    public class WorkLog
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public DateTime WorkDate { get; set; }

        public DateTime Time { get; set; }
        public string WorkType { get; set; }

        public string Title { get; set; }
        public string Description { get; set; }

        public string DepartmentName { get; set; }
    
        public string Status { get; set; }

        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string LocationName { get; set; }
        public string ImageUrl { get; set; }
       
    }
    public class Warning
    {
        [Key]
        public int WarningId { get; set; }

        [Required]
        public int SenderId { get; set; }

        [Required]
        public int ReceiverId { get; set; }

        [Required]
       
        public string Title { get; set; }

        [Required]
        public string Message { get; set; }

        [Required]
       
        public string Severity { get; set; } 

        [Required]
        public int EscalationLevel { get; set; } 

        [Required]
      
        public string Status { get; set; } = "Active"; 

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        public bool IsRead { get; set; } = false;
    }
    public class Notification
{
    [Key]
    public long Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Message { get; set; } = string.Empty;

    public long? SenderId { get; set; }

    [Required]
    public long? ReceiverId { get; set; }

    public bool IsBroadcast { get; set; } = false;

   public string? RelatedId { get; set; }

    public bool IsRead { get; set; } = false;

    public DateTime? ReadAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
    public class MonthlyProductivity
    {
        public int Id { get; set; }

        public int StaffId { get; set; }

        public int Month { get; set; }

        public int Year { get; set; }

        // Task /40 or /70
        public decimal TaskPoints { get; set; }

        // Goal /30 or /70
        public decimal GoalPoints { get; set; }

        // Attitude & Behaviour /15
        public decimal AttitudeScore { get; set; }

        // Task removal penalty
        public decimal TaskPenaltyPoints { get; set; }

        // Productivity /85
        public decimal Productivity { get; set; }

        // Final score /100
        public decimal TotalScore { get; set; }
    }
    public class FiveSPoints
    {
        public int Id { get; set; }
        public string Department { get; set; }

        public int Year { get; set; }
        public int Month { get; set; }
        public int Week { get; set; } 

        public decimal Points { get; set; }

    }

    public class QualityMetrics
    {
        public int Id { get; set; }

        public int StaffId { get; set; }

        public DateTime DateTime { get; set; }

        public int TotalWork { get; set; }

        public int Complaints { get; set; }

        public double Points { get; set; }

    }

    public class LeaveForm
    {
        public int Id { get; set; }
        public int SenderId { get; set; }
        public int ReceiverId { get; set; }

        public string? Name { get; set; }
        public string? Designation { get; set; }
        public string? Reason { get; set; }

        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }

        public string? LeaveType { get; set; }
        public decimal? TotalDays { get; set; }

        public string? ContactNumber { get; set; }

        public string? Status { get; set; }    
        public DateTime? ApprovedDate { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime? SubmittedDate { get; set; }
        public string? ApplicationSource { get; set; }
        public string LeaveTyp { get; set; }
        public int? CompensationExtraWorkId { get; set; }
    }

    public class PermissionForm
    {
        public int Id { get; set; }

        public int SenderId { get; set; }
        public int ReceiverId { get; set; }
        public string? Name { get; set; }
        public string? Designation { get; set; }
        public string? Reason { get; set; }

        public DateTime Date { get; set; }

        public TimeSpan FromTime { get; set; }
        public TimeSpan ToTime { get; set; }

        public decimal TotalHours { get; set; }

        public string? Status { get; set; }
        public DateTime? SubmittedDate { get; set; }
    }

    public class UserProfile
    {
        public int Id { get; set; }
        public int UserId { get; set; }

        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? BloodGroup { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? ContactNumber { get; set; }
        public string? EmergencyContact { get; set; }
        public string? Address { get; set; }

        public string? EmployeeId { get; set; }
        public string? Designation { get; set; }
        public string? Department { get; set; }
        public DateTime? DateOfJoining { get; set; }
        public string? ReportingManager { get; set; }

        public string? ProfileImage { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
    public class OverTime
    {
        public int Id { get; set; }

        public int Uid { get; set; }
        public string Dept { get; set; }
       
        public DateTime Date { get; set; }

        public TimeSpan FromTime { get; set; }
        public TimeSpan ToTime { get; set; }

        public decimal TotalHours { get; set; }
        public string Reason { get; set; } 
        public string Approved_by { get; set; }
        public bool isApprov { get; set; }
    }

    public class TaskMemberRemoval
    {
        [Key]
        public int Id { get; set; }

        public string TaskCode { get; set; }

        public int UserId { get; set; }

        public decimal PenaltyPoints { get; set; }

        public string? Reason { get; set; }

        public int RemovedBy { get; set; }

        public DateTime RemovedDate { get; set; }

        public bool IsPenaltyApplied { get; set; }
    }

    public class Roles
    {
        public int Id { get; set; }

        public string RoleName { get; set; }
        public int Position { get; set; }

        public bool Status { get; set; } 
    }

    public class ExtraWork
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public DateTime WorkedDate { get; set; }

        public string WorkType { get; set; }
        // WeeklyOff, PublicHoliday, CompanyHoliday, Other

        public TimeSpan StartTime { get; set; }

        public TimeSpan EndTime { get; set; }

        public decimal TotalHours { get; set; }

        public string Reason { get; set; } 

        public string Status { get; set; }
        // Pending, Approved, Rejected

        public int? ApprovedBy { get; set; }
        public bool IsCompensationUsed { get; set; } 

    }

    public class PunchCorrection
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public DateTime Date { get; set; }

        public string CorrectionType { get; set; }

        public TimeSpan PunchTime { get; set; }

        public string Reason { get; set; } 

        public string Status { get; set; } 

        public int? ApprovedById { get; set; }

    }

    public class AttitudeBehaviourScore
    {
        public int Id { get; set; }

        public int StaffId { get; set; }

        public string Department { get; set; }
        public DateTime Date { get; set; }

        public int Communication { get; set; }

        public int Punctuality { get; set; }

        public int Integrity { get; set; }

        public int Total { get; set; }

       
    }

    //---------------------------------------------------------------------------------//



    public class VerifyOtp
    {
        public string EmailorName { get; set; }
        public string Otp { get; set; }
    }
    public class CreateUser
    {
        public int UserId { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Department { get; set; }
        public string Role { get; set; }
    }
    public class ApproveUser
    {
        public int UserId { get; set; }
        public bool Approve { get; set; }
    }
    public class CreateTaskDto
    {
        public string Task { get; set; }
        public string Description { get; set; }
        public string Priority { get; set; }
        public string GoalCode { get; set; }
        public DateTime Start_date { get; set; }
        public DateTime Due_Date { get; set; }
        public List<int> AssignedToIds { get; set; } = new();
        public string PerformanceType { get; set; }

        public int? Quantity { get; set; }

        public TimeSpan? StartTime { get; set; }

        public TimeSpan? EndTime { get; set; }
    }
    public class UpdateTaskStatusDto
    {
        public string TaskCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
    public class StatusUpdateDto
    {
        public string Status { get; set; }
    }
    public class EditTaskDto
    {
        public string TaskCode { get; set; }
        public string Task { get; set; }
        public string Description { get; set; }
        public string Priority { get; set; }
        public DateTime Due_Date { get; set; }
        public List<int> AssignedToIds { get; set; }
        public List<RemoveTaskMemberDto> RemovedMembers { get; set; }
 
        public int? Quantity { get; set; }

        public TimeSpan? StartTime { get; set; }

        public TimeSpan? EndTime { get; set; }
    }
    public class UpdateUserDto
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string Department { get; set; }
    }
    public class ReviewTaskDto
    {
        public string TaskCode { get; set; }

        public bool IsDelayJustified { get; set; }

        public int? ManagerPoints { get; set; }

        public string? DelayReason { get; set; }

        public string? Comment { get; set; }
    }
    public class CreateWorkLogDto
    {
        public DateTime WorkDate { get; set; }

        // Actual work title
        public string Title { get; set; }

        // IN or OUT
        public string WorkType { get; set; }

        public string Description { get; set; }

        public bool IsSubmit { get; set; }

        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public string LocationName { get; set; }

        public IFormFile? Image { get; set; }
    }

    public class UpdateGoalDto
    {
        public string? Title { get; set; }
        public string? Priority { get; set; }
        public DateTime? DueDate { get; set; }
    }

    public class UpdateUserProfileDto
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? BloodGroup { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? ContactNumber { get; set; }
        public string? EmergencyContact { get; set; }
        public string? Address { get; set; }

        public string? EmployeeId { get; set; }
        public string? Designation { get; set; }
        public string? Department { get; set; }
        public DateTime? DateOfJoining { get; set; }
        public string? ReportingManager { get; set; }
        public IFormFile? ProfileImage { get; set; }
    }

    public class CreateOverTimeDto
    {
        public int Uid { get; set; }
        public string Dept { get; set; }
        public DateTime Date { get; set; }

        public string FromTime { get; set; }
        public string ToTime { get; set; }
        public string Reason { get; set; } 
    }
    public class ApproveOverTimeDto
    {
        public bool IsApproved { get; set; }
    }

    public class RemoveTaskMemberDto
    {
        public int UserId { get; set; }

        public string? Reason { get; set; }
    }

    public class CreateExtraWorkDto
    {
        public DateTime WorkedDate { get; set; }

        public string WorkType { get; set; }

        public TimeSpan StartTime { get; set; }

        public TimeSpan EndTime { get; set; }

        public string Reason { get; set; }
    }
    public class UpdateExtraWorkStatusDto
    {
        public string Status { get; set; } 
    }

    public class PunchCorrectionDto
    {
        public int UserId { get; set; }

        public DateTime Date { get; set; }

        public string CorrectionType { get; set; } 

        public TimeSpan PunchTime { get; set; }

        public string Reason { get; set; }
    }

    public class PunchCorrectionActionDto
    {
        public bool Approved { get; set; }

    }

    public class AttitudeBehaviourScoreDto
    {
        public int StaffId { get; set; }

        public int Communication { get; set; }

        public int Punctuality { get; set; }

        public int Integrity { get; set; }

        public DateTime Date { get; set; }
    }
}


