namespace Cike.MultiTenancy;

public interface ICurrentTenantAccessor
{
    long GetTenantId();
    void SetTenantId(long tenantId);
}
