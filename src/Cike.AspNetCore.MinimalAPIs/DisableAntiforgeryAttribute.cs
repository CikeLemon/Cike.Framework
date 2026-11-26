namespace Cike.AspNetCore.MinimalAPIs;

/// <summary>
/// 关闭端点的 ASP.NET Core Minimal API antiforgery 校验要求。
/// <para>
/// 背景：.NET 8 起，带表单参数（<see cref="IFormFile"/> / [FromForm] 等）的 Minimal API 端点
/// 会被平台自动加上 antiforgery 元数据；若管道未挂 <c>app.UseAntiforgery()</c>，
/// 请求时 <c>EndpointMiddleware</c> 直接抛 <see cref="InvalidOperationException"/>
/// （"contains anti-forgery metadata, but a middleware was not found that supports anti-forgery"）。
/// </para>
/// <para>
/// 本特性由框架在端点映射时识别，等价于对该端点调用 <c>.DisableAntiforgery()</c>，
/// 解除上述要求（不是启用 token 校验）。MVC 的 <c>[IgnoreAntiforgeryToken]</c>
/// 只实现 MVC 过滤器接口，对 Minimal API 端点无效，不要指望它生效。
/// </para>
/// <para>
/// 可标注在端点方法上（豁免单个端点）或端点类上（豁免该服务全部端点），命中任意一处即生效。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class DisableAntiforgeryAttribute : Attribute
{
}
