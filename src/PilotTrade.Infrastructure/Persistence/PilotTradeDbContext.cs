namespace PilotTrade.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

public class PilotTradeDbContext : DbContext
{
    public PilotTradeDbContext(DbContextOptions<PilotTradeDbContext> options) : base(options) { }

    public DbSet<BrokerEnvironmentEntity> BrokerEnvironments => Set<BrokerEnvironmentEntity>();
    public DbSet<InstrumentEntity> Instruments => Set<InstrumentEntity>();
    public DbSet<InstrumentSelectionEntity> InstrumentSelections => Set<InstrumentSelectionEntity>();
    public DbSet<OrderIntentEntity> OrderIntents => Set<OrderIntentEntity>();
    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();
    public DbSet<DecisionEntity> Decisions => Set<DecisionEntity>();
    public DbSet<ClaudeAnalysisEntity> ClaudeAnalyses => Set<ClaudeAnalysisEntity>();
    public DbSet<JevEvaluationEntity> JevEvaluations => Set<JevEvaluationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BrokerEnvironmentEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<InstrumentEntity>().HasKey(x => new { x.Epic, x.Environment, x.AccountId });
        modelBuilder.Entity<InstrumentSelectionEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<OrderIntentEntity>().HasKey(x => x.IntentId);
        modelBuilder.Entity<AuditEventEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<DecisionEntity>().HasKey(x => x.DecisionId);
        modelBuilder.Entity<ClaudeAnalysisEntity>().HasKey(x => x.AnalysisId);
        modelBuilder.Entity<JevEvaluationEntity>().HasKey(x => x.EvaluationId);
    }
}