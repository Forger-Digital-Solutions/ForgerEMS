using System.Linq;
using System.Net.Http;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class UserAgentHeaderProbeTests
{
    [Fact]
    public async Task GitHubReleaseUpdateCheckService_SendsUserAgentOnReleaseListRequest()
    {
        string? capturedUserAgent = null;
        var handler = new CapturingHandler(req =>
        {
            if (req.Headers.UserAgent.Count > 0)
            {
                capturedUserAgent = string.Join(" ", req.Headers.UserAgent.Select(p => p.ToString()));
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
            };
        });
        using var http = new HttpClient(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        _ = await service.CheckForNewerReleaseAsync("1.2.4-preview.2", null);
        Assert.False(string.IsNullOrWhiteSpace(capturedUserAgent));
        Assert.Contains("ForgerEMS", capturedUserAgent!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(AppReleaseInfo.Version, capturedUserAgent!, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveUpdateUserAgent_IncludesVersionAndRepoContactUrl()
    {
        var ua = GitHubReleaseUpdateCheckService.ResolveUpdateUserAgent();
        Assert.Contains(AppReleaseInfo.Version, ua, StringComparison.Ordinal);
        Assert.Contains("github.com/Forger-Digital-Solutions/ForgerEMS", ua, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly System.Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public CapturingHandler(System.Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            System.Threading.CancellationToken cancellationToken) =>
            System.Threading.Tasks.Task.FromResult(_respond(request));
    }
}
