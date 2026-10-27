namespace Cike.OpenIddict.Tests.Infrastructure;

/// <summary>
/// <see cref="IPermissionChecker"/> 测试桩：判定结果与传参由测试用例控制/观测。
/// </summary>
public class TestPermissionChecker(Func<bool> grant, Action<string> onChecking) : IPermissionChecker
{
    public Task<bool> IsGrantedAsync(string permission)
    {
        onChecking(permission);
        return Task.FromResult(grant());
    }
}
