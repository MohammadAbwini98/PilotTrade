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
    public DbSet<ExecutionStateEntity> ExecutionStates => Set<ExecutionStateEntity>();
    public DbSet<OrderJournalEntity> OrderJournals => Set<OrderJournalEntity>();
    public DbSet<WatchlistEntity> Watchlists => Set<WatchlistEntity>();
    public DbSet<DecisionEventEntity> DecisionEvents => Set<DecisionEventEntity>();
    public DbSet<OrderStateTransitionEntity> OrderStateTransitions => Set<OrderStateTransitionEntity>();

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
        modelBuilder.Entity<ExecutionStateEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<OrderJournalEntity>().HasKey(x => x.CommandId);
        modelBuilder.Entity<WatchlistEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<WatchlistEntity>().Property(x => x.Version).IsConcurrencyToken();
        modelBuilder.Entity<DecisionEventEntity>().HasKey(x => x.DecisionId);
        modelBuilder.Entity<DecisionEventEntity>().HasIndex(x => new { x.Environment, x.AccountId, x.RecordedTicks });
        modelBuilder.Entity<OrderStateTransitionEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<OrderStateTransitionEntity>().HasIndex(x => x.CommandId);
        modelBuilder.Entity<OrderJournalEntity>().HasIndex(x => new { x.Environment, x.AccountId });
    }
}