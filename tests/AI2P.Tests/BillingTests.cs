using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>Биллинг (ТЗ v1.15, todo18): сбор стоимости работы, поля и фильтры.</summary>
public sealed class BillingTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    /// <summary>Создать проект, ИИ-исполнителя, команду, задачу и завершённое задание с токенами.</summary>
    private (Project project, Team team, Executor ai, TaskItem task) ArrangeAiJob(
        double cost, long inTok, long outTok)
    {
        _f.Models.Seed();
        var model = _f.Models.List().Single(m => m.Name == "DeepSeek-V4-Flash");
        var project = _f.Projects.Create("Проект", null, null, null);
        var ai = _f.Executors.Create(new Executor { Nick = "jon", Kind = ExecutorKind.Ai, ModelId = model.Id }, null);
        var team = _f.Teams.Create(new Team
        {
            Name = "Команда",
            ProjectId = project.Id,
            Members = [new TeamMember { ExecutorId = ai.Id }],
        }, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id,
            TeamId = team.Id,
            Title = "Задача",
            ExecutorIds = [ai.Id],
        }, "тело", "", null);

        var job = _f.Jobs.Create(task.Id, ai.Id, task.DescriptionPath, null);
        _f.Jobs.SetState(job.Id, JobState.Running, null);
        _f.Jobs.SetState(job.Id, JobState.Done, ai.Id, "res.md", cost, inTok, outTok, "USD");
        return (project, team, ai, task);
    }

    [Fact]
    public void Records_Carry_All_Billing_Fields()
    {
        var (project, team, ai, task) = ArrangeAiJob(0.0021, 4780, 778);

        var record = Assert.Single(_f.Billing.Query());
        Assert.Equal(project.Id, record.ProjectId);
        Assert.Equal("Проект", record.ProjectName);
        Assert.Equal(team.Id, record.TeamId);
        Assert.Equal("Команда", record.TeamName);
        Assert.Equal(ai.Id, record.ExecutorId);
        Assert.Equal("jon", record.ExecutorNick);
        Assert.Equal("DeepSeek-V4-Flash", record.InternalName);       // внутреннее имя = модель
        Assert.Equal(ExecutorKind.Ai, record.Kind);
        Assert.Equal(task.DisplayId, record.TaskDisplayId);
        Assert.Equal(0.0021, record.Cost);
        Assert.Equal("USD", record.Currency);
        Assert.Equal(4780, record.InputTokens);
        Assert.Equal(778, record.OutputTokens);
        Assert.True(record.DurationSeconds >= 0);
        Assert.NotEqual(default, record.StartedAt);
    }

    [Fact]
    public void Filters_By_Project_Team_Executor_And_Model()
    {
        var (project, team, ai, _) = ArrangeAiJob(0.01, 100, 50);
        var modelId = _f.Executors.Get(ai.Id)!.ModelId;

        Assert.Single(_f.Billing.Query(projectId: project.Id));
        Assert.Single(_f.Billing.Query(teamId: team.Id));
        Assert.Single(_f.Billing.Query(executorId: ai.Id));
        Assert.Single(_f.Billing.Query(modelId: modelId));           // внутреннее имя ИИ = выбор по модели
        Assert.Empty(_f.Billing.Query(projectId: "нет-такого"));
    }

    [Fact]
    public void Filters_By_Period()
    {
        ArrangeAiJob(0.01, 100, 50);

        Assert.Single(_f.Billing.Query(from: DateTime.UtcNow.AddMinutes(-5)));
        Assert.Single(_f.Billing.Query(to: DateTime.UtcNow.AddMinutes(5)));
        Assert.Empty(_f.Billing.Query(from: DateTime.UtcNow.AddMinutes(5)));   // будущее — записей нет
    }

    [Fact]
    public void Queued_Job_Without_Start_Is_Not_Billed()
    {
        _f.Models.Seed();
        var model = _f.Models.List().First();
        var project = _f.Projects.Create("Проект", null, null, null);
        var ai = _f.Executors.Create(new Executor { Nick = "bot", Kind = ExecutorKind.Ai, ModelId = model.Id }, null);
        var team = _f.Teams.Create(new Team
        {
            Name = "К", ProjectId = project.Id, Members = [new TeamMember { ExecutorId = ai.Id }],
        }, null);
        var task = _f.Tasks.Create(new TaskItem
        {
            ProjectId = project.Id, TeamId = team.Id, Title = "З", ExecutorIds = [ai.Id],
        }, "", "", null);
        _f.Jobs.Create(task.Id, ai.Id, task.DescriptionPath, null); // queued, started_at не задан

        Assert.Empty(_f.Billing.Query());                            // в биллинг не попадает
    }
}
