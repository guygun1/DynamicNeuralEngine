using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

internal static class WebLearningChecks
{
    public static async Task Run(string scratch)
    {
        CorroborationChecks.Run();
        using var brain = new NeuralEngine();
        brain.TrainDialogue("what is a <unk>?", "i don't know.");
        var provider = new SourceProvider();
        using var client = new HttpClient(provider);
        var skills = new SkillRegistry();
        var search = new WebSearchSkill(client, "test-key");
        skills.Register(search);
        skills.Register(new WebLearnSkill(search, client));
        var context = new SkillContext("learner", (query, maximum) => brain.Generate(query, maximum), brain.TrainText,
            learnDialogue: brain.TrainDialogue, learnFromSource: brain.LearnFromSource);
        ModelAnswer Local(string query) {
            string text = brain.Chat(query);
            return new ModelAnswer(text, brain.AnswerState == 1, brain.AnswerState == 2, brain.ConversationTopic);
        }
        const string question = "what is a zarpleaf?";
        var unknown = Local(question);
        Check(!unknown.Supported && unknown.CanSearch && provider.Calls == 0, "An unsupported question is identified by native evidence state, not uncertainty wording");
        var learned = await LiveWebLearner.AnswerAsync(question, unknown, skills, context, Local);
        Check(learned.LearnedFromWeb && learned.Answer.Supported && learned.Answer.Text.Contains("Zarpleaf", StringComparison.OrdinalIgnoreCase) &&
            learned.Answer.Text.Contains("https://example.org/zarpleaf") && learned.Answer.Text.Contains("https://independent.net/zarpleaf"),
            "The live loop retains only corroborated claims and cites two different sites");
        Check(!learned.Answer.Text.Contains("flowers", StringComparison.OrdinalIgnoreCase), "A detail reported by only one site is not retained as confirmed knowledge");
        int calls = provider.Calls;
        int nodes = brain.NeuronCount;
        var recalled = await LiveWebLearner.AnswerAsync(question, Local(question), skills, context, Local);
        Check(!recalled.LearnedFromWeb && provider.Calls == calls && brain.NeuronCount == nodes, "A repeated question reuses live-learned knowledge without another search or new neurons");
        string deduction = brain.Generate("is a zarpleaf a living organism?");
        Check(deduction.Contains("source premises") && deduction.Contains("living organism") && deduction.Contains("https://example.org/zarpleaf"),
            "Controlled premises learned from a source support a new attributed multi-hop conclusion: " + deduction);
        Check(brain.WordNeuronId("https") < 0, "Citation URLs are retained as metadata rather than arbitrary word neurons");
        string path = Path.Combine(scratch, "web-learning.brain");
        Check(brain.SaveState(path), "Live source knowledge and premise provenance save in one compact file");
        using var restored = new NeuralEngine();
        Check(restored.LoadState(path) && restored.Generate(question).Contains("https://example.org/zarpleaf") &&
            restored.Generate(question).Contains("https://independent.net/zarpleaf") && restored.Generate("is a zarpleaf a living organism?").Contains("https://example.org/zarpleaf"),
            "Both live-learned reference text and source-backed reasoning survive restart");

        using var wikiClient = new HttpClient(new WikipediaProvider());
        var wiki = new WebSearchSkill(wikiClient, apiKey: "", useWikipediaFallback: true);
        var result = await wiki.ExecuteAsync(new SkillContext(), SkillContext.Schema("""{"query":"solar eclipse","count":1}"""), CancellationToken.None);
        Check(result.Ok && JsonSerializer.Serialize(result.Data).Contains("wikipedia-reference-search"), "A key-free reference-search fallback is clearly labelled as Wikipedia");

        using var onlyClient = new HttpClient(new SourceProvider(singleSite: true));
        var onlySearch = new WebSearchSkill(onlyClient, "test-key");
        int before = brain.NeuronCount;
        var unconfirmed = await new WebLearnSkill(onlySearch, onlyClient).ExecuteAsync(context,
            SkillContext.Schema("""{"query":"zarpleaf","question":"another unconfirmed question"}"""), CancellationToken.None);
        Check(unconfirmed.Ok && JsonSerializer.Serialize(unconfirmed.Data).Contains("tentative-single-source") && brain.NeuronCount > before,
            "A single readable website yields a clearly-labelled best-effort answer instead of nothing");
        using var redirectedClient = new HttpClient(new SourceProvider(redirectSecond: true));
        var redirected = await new WebLearnSkill(new WebSearchSkill(redirectedClient, "test-key"), redirectedClient).ExecuteAsync(context,
            SkillContext.Schema("""{"query":"zarpleaf"}"""), CancellationToken.None);
        Check(redirected.Ok && JsonSerializer.Serialize(redirected.Data).Contains("tentative"),
            "Two links that redirect to one site fall back to a labelled best-effort answer instead of corroboration");

        using var referenceClient = new HttpClient(new WikiReferenceProvider());
        var referenceSearch = new WebSearchSkill(referenceClient, apiKey: "", useWikipediaFallback: true);
        string learnedSources = "";
        var referenceContext = new SkillContext("reference-test", (_, _) => "", learnFromSource: (_, _, source) => learnedSources = source);
        var crossChecked = await new WebLearnSkill(referenceSearch, referenceClient).ExecuteAsync(referenceContext,
            SkillContext.Schema("""{"query":"zarpleaf"}"""), CancellationToken.None);
        Check(crossChecked.Ok && learnedSources.Contains("en.wikipedia.org") && learnedSources.Contains("reference.org"),
            "Key-free learning follows Wikipedia external references to cross-check a second website");
    }

    private sealed class SourceProvider : HttpMessageHandler
    {
        private readonly bool _singleSite;
        private readonly bool _redirectSecond;
        public SourceProvider(bool singleSite = false, bool redirectSecond = false) { _singleSite = singleSite; _redirectSecond = redirectSecond; }
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            bool search = request.RequestUri!.Host == "api.search.brave.com";
            Check(search || !request.Headers.Contains("X-Subscription-Token"), "Provider credentials are not forwarded to source pages");
            if (search) Check(request.RequestUri.Query.Contains("q=zarpleaf"), "Live search uses the inferred topic rather than question boilerplate");
            string sources = _singleSite ? "" : """,{"title":"Zarpleaf reference","url":"https://independent.net/zarpleaf","description":"A second fictional reference."}""";
            string extra = request.RequestUri.Host == "independent.net" ? "This independent zarpleaf reference describes a distinct study of the fictional organism." : "The first zarpleaf reference additionally states that a zarpleaf has flowers.";
            string body = search ? """{"web":{"results":[{"title":"Zarpleaf","url":"https://example.org/zarpleaf","description":"A fictional botany reference."}""" + sources + "]}}" :
                "<html><nav>irrelevant navigation</nav><article><p>A zarpleaf is a plant. A plant is a living organism. A plant needs water. " + extra + "</p></article></html>";
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, search ? "application/json" : "text/html") };
            if (!search && _redirectSecond && request.RequestUri.Host == "independent.net") response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.org/redirected");
            return Task.FromResult(response);
        }
    }

    private sealed class WikipediaProvider : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"query":{"search":[{"title":"Solar eclipse","snippet":"The Moon passes in front of the Sun."}]}}""", Encoding.UTF8, "application/json") });
    }

    private sealed class WikiReferenceProvider : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const string common = "A zarpleaf is a plant. A plant is a living organism. A plant needs water. ";
            string body;
            if (request.RequestUri!.Query.Contains("list=search")) body = """{"query":{"search":[{"title":"Zarpleaf","snippet":"A fictional reference entry."}]}}""";
            else if (request.RequestUri.Query.Contains("prop=extlinks")) body = """{"query":{"pages":[{"extlinks":[{"url":"https://reference.org/zarpleaf"}]}]}}""";
            else if (request.RequestUri.Query.Contains("prop=extracts")) body = JsonSerializer.Serialize(new { query = new { pages = new[] { new { extract = common + "The zarpleaf encyclopedia entry includes an original description of its fictional roots." } } } });
            else body = "<p>" + common + "The external zarpleaf publisher records a different field description of this fictional plant.</p>";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, request.RequestUri.Host == "en.wikipedia.org" ? "application/json" : "text/html") });
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"[PASS] {message}");
    }
}
