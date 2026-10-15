using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public sealed record ModelAnswer(string Text, bool Supported, bool CanSearch, string Topic);
public sealed record LiveAnswer(ModelAnswer Answer, bool LearnedFromWeb, string? LearningError = null, bool Tentative = false);

public static class LiveWebLearner
{
    // The native inference state, not an English phrase match, identifies a gap.
    // Tool use remains a provided policy; source acquisition is not a general planner.
    public static async Task<LiveAnswer> AnswerAsync(string prompt, ModelAnswer local, SkillRegistry skills,
        SkillContext context, Func<string, ModelAnswer> answerAfterLearning, CancellationToken cancellationToken = default)
    {
        if (!local.CanSearch || prompt.Length > 1000 || prompt.Contains("```")) return new LiveAnswer(local, false);
        string question = prompt.Trim();
        if (!string.IsNullOrWhiteSpace(local.Topic) && (question.Equals("explain", StringComparison.OrdinalIgnoreCase) ||
            question.Equals("tell me more", StringComparison.OrdinalIgnoreCase) || question.Equals("elaborate", StringComparison.OrdinalIgnoreCase)))
            question = "explain " + local.Topic;
        string query = string.IsNullOrWhiteSpace(local.Topic) ? question : local.Topic;
        var result = await skills.ExecuteAsync("web.learn", context, JsonSerializer.SerializeToElement(new { query, question }), cancellationToken);
        if (!result.Ok) return new LiveAnswer(local, false, result.Error);
        bool tentative = result.Data is not null &&
            JsonSerializer.SerializeToElement(result.Data).TryGetProperty("confidence", out var confidence) &&
            confidence.ValueKind == JsonValueKind.String && confidence.GetString() == "tentative";
        return new LiveAnswer(answerAfterLearning(question), true, null, tentative);
    }
}
