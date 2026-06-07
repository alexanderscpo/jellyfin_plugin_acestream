using System.Net.Http.Json;
using System.Text.Json;
using Jellyfin.Plugin.AceStream.Application;
using Jellyfin.Plugin.AceStream.Domain;

namespace Jellyfin.Plugin.AceStream.Infrastructure.Engine;

/// <summary>
/// <see cref="ISearchPort"/> adapter backed by the AceStream engine's HTTP <c>/search</c> node.
/// Translates the engine's grouped, snake_case JSON into flat domain <see cref="AceChannel"/>s.
/// The engine base URL is read per request from <see cref="IEngineSettings"/> so configuration
/// changes take effect without rebuilding this (potentially long-lived) client.
/// </summary>
public sealed class EngineSearchClient : ISearchPort
{
    private const int MaxPageSize = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly IEngineSettings _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="EngineSearchClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client used to call the engine.</param>
    /// <param name="settings">Provides the current engine base URL.</param>
    public EngineSearchClient(HttpClient httpClient, IEngineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(settings);
        _httpClient = httpClient;
        _settings = settings;
    }

    /// <inheritdoc />
    public async Task<SearchResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var baseUrl = _settings.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("The AceStream engine URL is not configured.");
        }

        var url = BuildRequestUrl(baseUrl, request);
        var dto = await _httpClient
            .GetFromJsonAsync<SearchResponseDto>(url, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return Map(dto);
    }

    private static string BuildRequestUrl(string baseUrl, SearchRequest request)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);
        var page = Math.Max(0, request.Page);

        var parameters = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            parameters.Add($"query={Uri.EscapeDataString(request.Query)}");
        }

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            parameters.Add($"category={Uri.EscapeDataString(request.Category)}");
        }

        parameters.Add($"page={page}");
        parameters.Add($"page_size={pageSize}");

        return $"{baseUrl.TrimEnd('/')}/search?{string.Join('&', parameters)}";
    }

    private static SearchResult Map(SearchResponseDto? dto)
    {
        var result = dto?.Result;
        if (result?.Results is null)
        {
            return new SearchResult(result?.Total ?? 0, Array.Empty<AceChannel>());
        }

        var channels = new List<AceChannel>();
        foreach (var group in result.Results)
        {
            if (group.Items is null)
            {
                continue;
            }

            foreach (var item in group.Items)
            {
                if (TryMapChannel(item, out var channel))
                {
                    channels.Add(channel);
                }
            }
        }

        return new SearchResult(result.Total, channels);
    }

    private static bool TryMapChannel(SearchItemDto item, out AceChannel channel)
    {
        channel = null!;

        if (item.Infohash is null || string.IsNullOrWhiteSpace(item.Name))
        {
            return false;
        }

        try
        {
            channel = new AceChannel(
                Infohash.Create(item.Infohash),
                item.Name,
                item.Status.ToChannelStatus(),
                Availability.Create(Math.Clamp(item.Availability, 0.0, 1.0)),
                item.Categories ?? (IReadOnlyList<string>)Array.Empty<string>(),
                item.Disabled,
                item.ChannelId,
                item.Countries,
                item.Languages,
                item.AvailabilityUpdatedAt > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(item.AvailabilityUpdatedAt)
                    : null);
            return true;
        }
        catch (ArgumentException)
        {
            // A malformed item (bad infohash, etc.) is skipped rather than failing the whole search.
            return false;
        }
    }
}
