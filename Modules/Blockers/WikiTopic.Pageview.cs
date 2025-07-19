using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WikiClientLibrary.Client;

namespace BoneBoard.Modules.Blockers;

internal partial class WikiTopic
{
    public class PageviewsResponse
    {
        public PageviewItem[] items { get; set; }
    }

    public class PageviewItem
    {
        public string project { get; set; }
        public string article { get; set; }
        public string granularity { get; set; }
        public string timestamp { get; set; }
        public string access { get; set; }
        public string agent { get; set; }
        public int views { get; set; }
    }

    public class PageviewResponseParser : WikiResponseMessageParser<PageviewsResponse>
    {
        public static readonly PageviewResponseParser Instance = new();

        public override async Task<PageviewsResponse> ParseResponseAsync(HttpResponseMessage response, WikiResponseParsingContext context)
        {
            response.EnsureSuccessStatusCode();
            var str = await response.Content.ReadAsStringAsync();

            return JsonConvert.DeserializeObject<PageviewsResponse>(str) ?? throw new Exception($"Deserialized string to null! This should not happen! {str}");
        }
    }

    public class PageviewRequest : WikiRequestMessage
    {
        public PageviewRequest(string? id) : base(id)
        {
        }

        public override HttpContent GetHttpContent()
        {
            return new StringContent("");
        }

        public override HttpMethod GetHttpMethod()
        {
            return HttpMethod.Get;
        }

        public override string? GetHttpQuery()
        {
            return null;
        }
    }

    public static async Task<PageviewsResponse> GetArticlePageviewsAsync(
        WikiClient client,
        string project          /* e.g. "en.wikipedia.org" */,
        string article          /* URI-encoded title */,
        DateOnly start = default    /* YYYYMMDD */,
        DateOnly end = default      /* YYYYMMDD */)
    {
        //"https://wikimedia.org/api/rest_v1/metrics/pageviews/per-article/en.wikipedia.org/all-access/user/Python_(programming_language)/daily/20210101/20210501"

        if (start == default)
            start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        if (end == default)
            end = DateOnly.FromDateTime(DateTime.UtcNow);
        var url = $"https://wikimedia.org/api/rest_v1/metrics/pageviews/per-article/{project}/all-access/user/{Uri.EscapeDataString(article)}/daily/{start:yyyyMMdd}/{end:yyyyMMdd}";
        // WikiClient.InvokeAsync<T> will send a GET to https://{host}/api/rest_v1/{url}
        return await client.InvokeAsync<PageviewsResponse>(url, new PageviewRequest("yeah"), PageviewResponseParser.Instance, default);
    }
}
