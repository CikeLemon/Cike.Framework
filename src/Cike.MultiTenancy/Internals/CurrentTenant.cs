namespace Cike.MultiTenancy.Internals;

internal class CurrentTenant(ICurrentTenantAccessor _currentTenantAccessor) : ICurrentTenant, ITransientDependency
{
    public long Id { get => _currentTenantAccessor.GetTenantId(); }

    public IDisposable Change(long tenantId)
    {
        var currentTenantId = _currentTenantAccessor.GetTenantId();

        _currentTenantAccessor.SetTenantId(tenantId);

        return new DisposeAction(() =>
        {
            _currentTenantAccessor.SetTenantId(currentTenantId);
        });
    }
}
