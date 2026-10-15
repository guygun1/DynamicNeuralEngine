# Extensible skills API

Skills live in the .NET application layer and are separate from the native neural
core. They expose names, argument schemas, model/workspace requirements, and
structured success/error results. Registering a skill does not by itself teach
the brain when or how to use it autonomously.

## Discovery and invocation

```powershell
.\dne.cmd skills
.\dne.cmd serve --workspace "."
```

- `GET /api/skills` lists metadata and JSON argument schemas.
- `POST /api/skills/execute` executes one registered skill.
- `POST /api/agent/tasks` executes an explicit multi-step workflow.

Example body:

```json
{"model":"don","skill":"brain.query","arguments":{"query":"does a robin need energy?"}}
```

The result has `ok`, `text`, `data`, and `error` fields. Model-independent skills
can omit `model`. Workspace skills require a workspace selected at startup; a
request cannot change that root.

In regular chat:

```text
/skills
/skill brain.query {"query":"23 * (17 - 5)"}
/skill brain.observe {"statement":"A wren is a bird."}
/skill web.search {"query":"NASA solar eclipse","count":3}
```

## Built-in skills

| Skill | Purpose |
| --- | --- |
| `brain.query` | Learned recall, controlled-fact inference, arithmetic |
| `brain.observe` | Explicit text or controlled-premise teaching |
| `web.search` | Wikipedia reference search or configured Brave web search |
| `web.learn` | Search, read relevant passages, retain citations and controlled premises |
| `workspace.list` | File listing, skipping generated directories |
| `workspace.read` | Text contents and SHA-256 hash |
| `workspace.search` | Literal text search |
| `workspace.write` | UTF-8 file writing and a displayed diff |
| `workspace.replace` | Exactly one text replacement, optional hash check |
| `workspace.run` | Explicit executable/argument-array invocation |
| `code.synthesize` | Bounded example-driven C# synthesis and verification |

`brain.observe` records caller-provided information. Search snippets alone are not
automatically accepted as facts. `web.learn` reads source content and retains
attribution. `--learn-web` enables a bounded missing-knowledge acquisition policy;
general autonomous tool planning remains a separate research task.

## Enable web search

The default registry provides key-free Wikipedia reference search. For broader
web search, set a Brave Search key before starting the process:

```powershell
$env:BRAVE_SEARCH_API_KEY = "your_key_here"
.\dne.cmd serve
```

The result identifies `wikipedia-reference-search` or `brave-web-search`. A key is
sent only to its search provider, not to source pages or brain storage. Results
contain titles, URLs, and snippets, not a claim of independent verification.

For source-backed learning:

```json
{"model":"don","skill":"web.learn","arguments":{"query":"quokka","question":"what is a quokka?"}}
```

`query` guides search; optional `question` is the model's recall context. By default
`min_sources` is 2 and `max_sites` is 4; 3 and 6 are also accepted. Requiring a
single site is not supported. The learner searches up to ten results and, when
needed, follows Wikipedia external references to reach other websites, reading at
most three sites concurrently with per-page deadlines and an eighteen-second
overall budget.

Only individually corroborated statements are learned, within a 3,000-character
budget. An unchecked paragraph is never copied merely because one sentence agrees.
Subdomains count as one publisher-site group, redirect destinations are used, and
identical page text does not provide another vote. All retained statements share
the listed supporting source cohort, so their citations stay accurate in the brain.
URLs and retrieval dates persist without becoming word neurons.

Controlled assertions and close wording can be compared, including explicit
negation and differing numeric claims. Disputed claims are withheld, including
when two sites agree but a third explicitly contradicts them. When no statement
reaches the corroboration threshold, the skill falls back to a **best-effort**
result instead of refusing: it keeps the most supported non-disputed statements,
or the single most relevant passage, labelled `tentative-single-source` or
`tentative-unstructured` with `confidence: tentative`. The chat reply then says it
could not confirm the answer. The result exposes `verification`, `confidence`,
`sites_read`, `sources`, `claims`, and `disagreements`. Only when no source could
be read at all does it return `ok: false` and leave the model unchanged.

This is conservative corroboration, not general natural-language entailment or
proof of truth. Domain grouping is a heuristic, different domains can share an
original publisher, and sophisticated copying or paraphrased contradictions may
not be recognized. A tentative result is deliberately unverified; check anything
important. Existing saved knowledge is not retroactively certified; use `web.learn`
with its original `question` to request a fresh check.

## Add a skill

Create a class implementing `IAgentSkill`. This clock returns an observation:

```csharp
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public sealed class UtcClockSkill : IAgentSkill
{
    public string Name => "clock.utc";
    public string Description => "Return the current UTC time.";
    public bool RequiresModel => false;
    public JsonElement Parameters => SkillContext.Schema("{\"type\":\"object\"}");

    public Task<SkillResult> ExecuteAsync(
        SkillContext context, JsonElement arguments, CancellationToken token)
        => Task.FromResult(new SkillResult(true, Data: new {
            utc = DateTime.UtcNow.ToString("O")
        }));
}
```

Register it in `SkillRegistry.CreateDefault()`:

```csharp
registry.Register(new UtcClockSkill());
```

Rebuild with `dne rebuild`. It appears in CLI/chat/API discovery and uses the same
execution endpoint. A skill declares `RequiresWorkspace` when it needs the scoped
file service. Its implementation validates arguments; the schema describes the
contract rather than providing an automatic full JSON-schema validator.

`SkillContext` provides optional model querying, observation, dialogue learning,
and a workspace service. A file or web skill need not receive a model. Learning
callbacks make writes explicit. Cancellation tokens propagate to asynchronous
providers and workspace command execution.
