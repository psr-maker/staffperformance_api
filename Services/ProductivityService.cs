using Microsoft.EntityFrameworkCore;
using staff;
using staff_work_tracking.Data;

namespace StaffWork_Track.Services
{
    public class ProductivityService
    {
        private readonly AppDbContext _context;

        public ProductivityService(AppDbContext context)
        {
            _context = context;
        }

        public async Task CalculateMonthly(int staffId, int month, int year)
        {

            // =====================================================
            // TASK SCORE
            // =====================================================

            var taskCodes = await _context.TaskMembers
                .Where(t =>
                    !string.IsNullOrEmpty(t.Assign_To) &&
                    t.Assign_To.StartsWith(staffId + "-"))
                .Select(t => t.TaskCode)
                .Distinct()
                .ToListAsync();

            var taskScores = await _context.TaskReview
                .Where(r =>
                    r.ReviewedAt.Month == month &&
                    r.ReviewedAt.Year == year &&
                    taskCodes.Contains(r.TaskCode))
                .Select(r => r.FinalPoints)
                .ToListAsync();

            bool hasTask = taskScores.Any();

            decimal monthlyTaskScore = hasTask
                ? (decimal)taskScores.Average()
                : 0m;


            // =====================================================
            // TASK REMOVAL PENALTY
            // =====================================================

            int taskPenaltyPoints = (int)(
                await _context.TaskMemberRemoval
                    .Where(x =>
                        x.UserId == staffId &&
                        x.IsPenaltyApplied &&
                        x.RemovedDate.Month == month &&
                        x.RemovedDate.Year == year)
                    .SumAsync(x => (decimal?)x.PenaltyPoints) ?? 0m
            );


            // Apply task removal penalty

            monthlyTaskScore = Math.Max(
                monthlyTaskScore - taskPenaltyPoints,
                0m
            );


            // =====================================================
            // GOAL SCORE
            // =====================================================

            var goalScores = await _context.Goal
                .Where(g =>
                    g.Assign_To == staffId.ToString() &&
                    g.Completed_Date.HasValue &&
                    g.Completed_Date.Value.Month == month &&
                    g.Completed_Date.Value.Year == year &&
                    g.Status != null &&
                    g.Status.Trim().ToLower() == "completed")
                .Select(g => g.Goalpoints)
                .ToListAsync();

            bool hasGoal = goalScores.Any();

            decimal monthlyGoalScore = hasGoal
                ? (decimal)goalScores.Average()
                : 0m;


            // =====================================================
            // TASK / GOAL WEIGHT
            // =====================================================

            decimal taskWeight = 0m;
            decimal goalWeight = 0m;

            if (hasTask && hasGoal)
            {
                // Both exist
                taskWeight = 45m;
                goalWeight = 40m;
            }
            else if (hasTask && !hasGoal)
            {
                // Only Task
                // Task gets complete 85 points
                taskWeight = 85m;
            }
            else if (!hasTask && hasGoal)
            {
                // Only Goal
                // Goal gets complete 85 points
                goalWeight = 85m;
            }


            // =====================================================
            // TASK POINTS
            // =====================================================

            decimal finalTaskPoints = hasTask
                ? (monthlyTaskScore / 100m) * taskWeight
                : 0m;


            // =====================================================
            // GOAL POINTS
            // =====================================================

            decimal finalGoalPoints = hasGoal
                ? (monthlyGoalScore / 100m) * goalWeight
                : 0m;

            // =====================================================
            // ATTITUDE & BEHAVIOUR /15
            // =====================================================

            decimal attitudeScore = await _context.AttitudeBehaviourScore
                .Where(x =>
                    x.StaffId == staffId &&
                    x.Date.Month == month &&
                    x.Date.Year == year)
                .Select(x => (decimal?)x.Total)
                .FirstOrDefaultAsync() ?? 0m;

            attitudeScore = Math.Clamp(
                attitudeScore,
                0m,
                15m
            );
            // =====================================================
            // PRODUCTIVITY /85
            // =====================================================

            decimal productivity =
                finalTaskPoints +
                finalGoalPoints;

            productivity = Math.Clamp(
                productivity,
                0m,
                85m
            );

            // =====================================================
            // TOTAL SCORE /100
            // =====================================================

            decimal totalScore =
                productivity +
                attitudeScore;

            totalScore = Math.Clamp(
                totalScore,
                0m,
                100m
            );

            // ================= SAVE =================

            var existing = await _context.MonthlyProductivity
                .FirstOrDefaultAsync(x =>
                    x.StaffId == staffId &&
                    x.Month == month &&
                    x.Year == year);

            if (existing != null)
            {
                _context.MonthlyProductivity.Remove(existing);
                await _context.SaveChangesAsync();
            }

            var data = new MonthlyProductivity
            {
                StaffId = staffId,
                Month = month,
                Year = year,

                TaskPoints = finalTaskPoints,
                GoalPoints = finalGoalPoints,

                AttitudeScore = attitudeScore,

                TaskPenaltyPoints = taskPenaltyPoints,

                Productivity = productivity,
                TotalScore = totalScore
            };

            _context.MonthlyProductivity.Add(data);
            await _context.SaveChangesAsync();
        }

    }
}