using System.Text.Json;

namespace Cike.AspNetCore.MinimalAPIs.Tests;

/// <summary>
/// TrimStringJsonConverter 回归测试：
/// JSON body 的 string 属性反序列化时自动 Trim（业务代码不再手写 .Trim()）；null 保持 null；
/// 查询串绑定不经过 JSON，前后空格必须原样保留（搜索关键词合法含空格），响应写出侧同样原样。
/// </summary>
public class JsonStringTrimTests : IClassFixture<MinimalApiTestWebHost>
{
    private readonly MinimalApiTestWebHost _host;

    public JsonStringTrimTests(MinimalApiTestWebHost host) => _host = host;

    [Fact]
    public async Task BodyStrings_AreTrimmed_OnRead()
    {
        using var client = new HttpClient();
        var (response, body) = await PostJsonAsync(client, "/echo/body", """{"name":"  padded  ","note":"   "}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("padded", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal("", doc.RootElement.GetProperty("note").GetString());
    }

    [Fact]
    public async Task BodyNullString_StaysNull()
    {
        using var client = new HttpClient();
        var (response, body) = await PostJsonAsync(client, "/echo/body", """{"name":null,"note":" x "}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("name").ValueKind);
        Assert.Equal("x", doc.RootElement.GetProperty("note").GetString());
    }

    [Fact]
    public async Task QueryStrings_AreNotTrimmed_AndResponseWriteIsVerbatim()
    {
        using var client = new HttpClient();
        var response = await client.GetAsync($"{_host.BaseUrl}/echo/query?keyword=%20a%20b%20");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        // 查询串绑定不 Trim + 序列化写出侧不 Trim，一次断言两个行为
        Assert.Equal(" a b ", doc.RootElement.GetProperty("keyword").GetString());
    }

    private async Task<(HttpResponseMessage Response, string Body)> PostJsonAsync(HttpClient client, string path, string json)
    {
        var response = await client.PostAsync($"{_host.BaseUrl}{path}", new StringContent(json, Encoding.UTF8, "application/json"));
        return (response, await response.Content.ReadAsStringAsync());
    }
}
