namespace Cike.MultiTenancy.Internals;

public class CurrentTenantAccessor : ICurrentTenantAccessor
{
    protected AsyncLocal<long> AsyncLocalTenantId { get; set; } = new AsyncLocal<long>();
    public long GetTenantId()
    {
        return AsyncLocalTenantId.Value;
    }

    public void SetTenantId(long tenantId)
    {
        AsyncLocalTenantId.Value = tenantId;
    }
}
