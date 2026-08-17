using FieldVisit.Domain.Entities;

namespace FieldVisit.Application;

public static class V170ProjectDateRules
{
    public static bool IsAvailableOn(
        Project project,
        DateOnly visitDate)
    {
        if (!project.IsActive)
            return false;

        if (project.StartDate.HasValue
            && visitDate < project.StartDate.Value)
            return false;

        if (project.EndDate.HasValue
            && visitDate > project.EndDate.Value)
            return false;

        return true;
    }

    public static void EnsureAvailableOn(
        Project project,
        DateOnly visitDate)
    {
        if (!project.IsActive)
            throw new InvalidOperationException(
                $"專案「{project.ProjectName}」已停用。");

        if (project.StartDate.HasValue
            && visitDate < project.StartDate.Value)
        {
            throw new InvalidOperationException(
                $"行程日期 {visitDate:yyyy-MM-dd} 早於專案「{project.ProjectName}」開始日期 {project.StartDate.Value:yyyy-MM-dd}。");
        }

        if (project.EndDate.HasValue
            && visitDate > project.EndDate.Value)
        {
            throw new InvalidOperationException(
                $"行程日期 {visitDate:yyyy-MM-dd} 晚於專案「{project.ProjectName}」結束日期 {project.EndDate.Value:yyyy-MM-dd}。");
        }
    }
}
