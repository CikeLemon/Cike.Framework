namespace Cike.MultiTenancy;

public interface ICurrentTenant
{
    long Id { get; }

    IDisposable Change(long tenantId);
}
