using System.Text;
using System.Text.Json;
using Penban.Services.Abstractions;

namespace Penban.Services;

/// <summary>
/// Posts feedback to the BugBear backend. The product key and the category ids are the public
/// identifiers of the Penban project there, so they are constants rather than configuration.
/// </summary>
public class FeedbackService : IFeedbackService
{
    private const string ApiUrl = "https://api.bug-bear.com/api/feedback";
    private const string ProductApiKey = "bb_96bdf8d5f8e04f018317f294ceaeada0";

    private static readonly Dictionary<FeedbackCategory, string> CategoryIds = new()
    {
        { FeedbackCategory.Ui,          "21bb2c06-2be9-436b-80d8-d5004888bc5b" },
        { FeedbackCategory.Ux,          "21132390-233b-419e-ad2f-caaa2e1ad6dd" },
        { FeedbackCategory.FeatureWish, "07c7a945-3e0e-4c5e-b145-f4ee7556762a" },
        { FeedbackCategory.General,     "2c2c6c19-355e-418d-8fb3-6c5efd395f70" },
    };

    /// <summary>One client for the whole app: feedback is rare, so a client per submit would only
    /// pay for a new connection every time.</summary>
    private static readonly HttpClient SharedClient = new();

    private readonly string version;

    public FeedbackService(string version) => this.version = version;

    public async Task<bool> SubmitFeedbackAsync(FeedbackCategory category, string description, string? submitterEmail = null)
    {
        var payload = new FeedbackRequest(
            ProductApiKey,
            CategoryIds[category],
            description,
            submitterEmail,
            $"{{\"version\":\"{version}\"}}",
            ProductVersionId: null);

        var json = JsonSerializer.Serialize(payload, FeedbackJsonContext.Default.FeedbackRequest);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await SharedClient.PostAsync(ApiUrl, content);
        return response.IsSuccessStatusCode;
    }
}
