using Cike.EventBus;
using System.Data.Common;

namespace Cike.Data.EFCore.Uow;

public class EFCoreUnitOfWork<TDbContext>(IServiceProvider _serviceProvider) : IUnitOfWork where TDbContext : CikeDbContext<TDbContext>
{
    private DbContext? _dbContext;

    public Guid TransactionId { get; set; }

    public DbContext DbContext
    {
        get
        {
            return _dbContext ??= _serviceProvider.GetRequiredService<TDbContext>();
        }
        private set
        {
            _dbContext = value;
        }
    }

    public bool IsTransactionBegun => DbContext.Database.CurrentTransaction is not null;

    public IsolationLevel? IsolationLevel { get; set; }

    public IDbTransaction DbTransaction
    {
        get
        {
            if (IsTransactionBegun)
                return DbContext.Database.CurrentTransaction!.GetDbTransaction();

            IDbContextTransaction transaction = IsolationLevel == null ? DbContext.Database.BeginTransaction() : DbContext.Database.BeginTransaction(IsolationLevel.Value);
            TransactionId = transaction.TransactionId;
            return transaction.GetDbTransaction();
        }
    }

    public UnitOfWorkCommitState CommitState { get; set; }

    public async Task BeginTransactionAsync(IsolationLevel? isolationLevel, CancellationToken cancellationToken = default)
    {
        if (IsTransactionBegun)
        {
            return;
        }
        IDbContextTransaction transaction = isolationLevel.HasValue ? await DbContext.Database.BeginTransactionAsync(isolationLevel.Value, cancellationToken) : await DbContext.Database.BeginTransactionAsync(cancellationToken);
        TransactionId = transaction.TransactionId;
        CommitState = UnitOfWorkCommitState.Uncommitted;
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        var queueEventBus = _serviceProvider.GetService<IQueueEventBus>();

        if (queueEventBus is not null)
        {
            while (await queueEventBus.AnyQueueAsync())
            {
                await queueEventBus.PublishQueueAsync();

                await DbContext.SaveChangesAsync(cancellationToken);
            }
        }

        if (CommitState == UnitOfWorkCommitState.Uncommitted)
        {
            await DbContext.SaveChangesAsync(cancellationToken);
            await DbContext.Database.CommitTransactionAsync(cancellationToken);
            CommitState = UnitOfWorkCommitState.Committed;
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (CommitState == UnitOfWorkCommitState.Uncommitted)
        {
            await DbContext.Database.RollbackTransactionAsync(cancellationToken);
            CommitState = UnitOfWorkCommitState.Rollbacked;
        }
    }
}
