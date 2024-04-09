using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RestSharp;
using RestSharp.Authenticators;
using ShortUrl.Models;

namespace ShortUrl.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private static string _token;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        var options = new RestClientOptions("http://localhost:3000/")
        {
            Authenticator = new HttpBasicAuthenticator("admin", "umami")
        };
        var client = new RestClient(options);
     
        var request = new RestRequest("api/auth/login").AddJsonBody(new { Username = "admin", Password = "umami" });

        var response = await client.PostAsync<Login>(request, CancellationToken.None);
        if (response == null)
        {
          return View("Error");
        }

        _token = response.Token;

        return View();
    }
 
    [HttpGet("/link/{token}")]
    public async Task<IActionResult> Link([FromRoute] string token)
    {
        var shortUrl = TokenStorage.GetUrl(token);
        if (shortUrl is null)
        {
            return NotFound();
        }

        var options = new RestClientOptions("http://localhost:3000/")
        {
            Authenticator = new JwtAuthenticator(_token)
        };
        var client = new RestClient(options);
        var response = await GetStats(shortUrl, client);
        var os = await GetMetrics(shortUrl, client, "os");
        var url = await GetMetrics(shortUrl, client, "url");
        var referrer = await GetMetrics(shortUrl, client, "referrer");
        var browser = await GetMetrics(shortUrl, client, "browser");
        var device = await GetMetrics(shortUrl, client, "device");
        var country = await GetMetrics(shortUrl, client, "country");
        var @event = await GetMetrics(shortUrl, client, "event");

        ViewBag.LinkStats = new LinkStats()
        {
            
            Os = new Value
            {
                Labels = string.Join(',', os.Select(x => $"'{x.x}'")),
                Values = string.Join(',', os.Select(x => x.y))
            },
            Device = new Value
            {
                Labels = string.Join(',', device.Select(x => $"'{x.x}'")),
                Values = string.Join(',', device.Select(x => x.y))
            },
            Referrer = new Value
            {
                Labels = string.Join(',', referrer.Select(x => $"'{x.x}'")),
                Values = string.Join(',', referrer.Select(x => x.y))
            },
            Url = new Value
            {
                Labels = string.Join(',', url.Select(x => $"'{x.x}'")),
                Values = string.Join(',', url.Select(x => x.y))
            },
            Browser = new Value
            {
                Labels = string.Join(',', browser.Select(x => $"'{x.x}'")),
                Values = string.Join(',', browser.Select(x => x.y))
            },
            Country = new Value
            {
                Labels = string.Join(',', country.Select(x => $"'{x.x}'")),
                Values = string.Join(',', country.Select(x => x.y))
            },
        };
        ViewBag.Url = shortUrl;
        return View(response);
    }

    private static async Task<Stats?> GetStats(ShortUrl shortUrl, RestClient client)
    {
        DateTime today = DateTime.Today;
        DateTime tomorrow = DateTime.Today.AddDays(1);

        long startAt = new DateTimeOffset(today).ToUnixTimeMilliseconds();
        long endAt = new DateTimeOffset(tomorrow).ToUnixTimeMilliseconds() - 1;

        var request = new RestRequest($"api/websites/{shortUrl.WebsiteId}/stats");
        request.AddParameter("startAt", startAt);
        request.AddParameter("endAt", endAt);

        var response = await client.GetAsync<Stats>(request, CancellationToken.None);
        return response;
    }

    private static async Task<List<Metric>> GetMetrics(ShortUrl shortUrl, RestClient client, string type)
    {
        DateTime today = DateTime.Today;
        DateTime tomorrow = DateTime.Today.AddDays(1);

        long startAt = new DateTimeOffset(today).ToUnixTimeMilliseconds();
        long endAt = new DateTimeOffset(tomorrow).ToUnixTimeMilliseconds() - 1;

        var request = new RestRequest($"api/websites/{shortUrl.WebsiteId}/metrics");
        request.AddParameter("startAt", startAt);
        request.AddParameter("endAt", endAt);
        request.AddParameter("type", type);

        var response = await client.GetAsync<List<Metric>>(request, CancellationToken.None);
        return response;
    }

    [HttpPost("SaveToken")]
    public async Task<IActionResult> SaveToken([FromForm] string url)
    {
        if (!IsValidUrl(url))
        {
            TempData["Error"] = "Invalid url";
            return RedirectToAction("Index");
        }
        
        var options = new RestClientOptions("http://localhost:3000/")
        {
            Authenticator = new JwtAuthenticator(_token)
        };
        var client = new RestClient(options);

        var request = new RestRequest("api/websites").AddJsonBody(new { domain = url, name = url });

        var response = await client.PostAsync<Website>(request, CancellationToken.None);

        var token = TokenGenerator.GenerateToken();
        TokenStorage.AddUrl(token, url, response.id);

        return RedirectToAction("Link", new
        {
            token
        });
    }

    [HttpGet("/{token}")]
    public IActionResult RedirectToUrl([FromRoute] string token)
    {
        var shortUrl = TokenStorage.GetUrl(token);
        if (shortUrl is null)
        {
            return NotFound();
        }

        ViewBag.Id = shortUrl.WebsiteId;
        ViewBag.Url = shortUrl.Url; 
         return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
    public bool IsValidUrl(string url, bool requireHttps = false)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri validatedUri))
        {
            if (validatedUri.Scheme == Uri.UriSchemeHttp || validatedUri.Scheme == Uri.UriSchemeHttps)
            {
                if (requireHttps)
                {
                    return validatedUri.Scheme == Uri.UriSchemeHttps;
                }
                return true;
            }
        }
        return false;
    }

}

class LinkStats
{
    public Value Os { get; set;}
public  Value Device { get; set;}
public   Value Referrer { get; set;}
public   Value       Url  { get; set;}
public                Value  Browser  { get; set;}
public               Value   Country  { get; set;}
}

class Value
{
    public string Labels { get; set; }
    public string Values { get; set; }
}

public class Metric
{
    public string x { get; set; }
    public int y { get; set; }
}

class ShortUrl
{
    public string Token { get; set; }
    public string Url { get; set; }
    public string WebsiteId { get; set; }
}

class TokenGenerator
{
    private static readonly Random Random = new Random();
    private const int TokenLength = 11;
    private const string Chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";


    public static string GenerateToken() => new string(Enumerable.Repeat(Chars, TokenLength)
        .Select(s => s[Random.Next(s.Length)]).ToArray());
}

class TokenStorage
{
    private static readonly Dictionary<string, ShortUrl> Urls = new();

    public static void AddUrl(string token, string url, string websiteId)
    {
        Urls[token] = new ShortUrl
        {
            Token = token,
            Url = url,
            WebsiteId = websiteId
        };
    }

    public static bool Exist(string token) => Urls.ContainsKey(token);

    public static ShortUrl? GetUrl(string token)
    {
        if (Exist(token))
        {
            return Urls[token];
        }

        return null;
    }
}

public class User
{
    public string Id { get; set; }
    public string Username { get; set; }
    public string CreatedAt { get; set; }
}

public class Login
{
    public string Token { get; set; }
    public User User { get; set; }
}

public class Website
{
    public string id { get; set; }
}

public class Bounces
{
    public int value { get; set; }
    public int change { get; set; }
}

public class Pageviews
{
    public int value { get; set; }
    public int change { get; set; }
}

public class Stats
{
    public Pageviews pageviews { get; set; }
    public Uniques uniques { get; set; }
    public Bounces bounces { get; set; }
    public Totaltime totaltime { get; set; }
}

public class Totaltime
{
    public int value { get; set; }
    public int change { get; set; }
}

public class Uniques
{
    public int value { get; set; }
    public int change { get; set; }
}