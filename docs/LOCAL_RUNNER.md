# Local model runner

The runner hosts DynamicNeuralEngine brains. It implements a small, documented
subset of Ollama-style commands and JSON endpoints using the engine's own learning
and inference code.

## Commands

```powershell
.\dne.cmd run don
.\dne.cmd ui current --workspace "." --learn-web
.\dne.cmd agent don --workspace "." --learn-web
.\dne.cmd list
.\dne.cmd show don
.\dne.cmd create another
.\dne.cmd create imported --from "existing.brain"
.\dne.cmd serve
.\dne.cmd serve --port 11436
.\dne.cmd simulate don --episodes 300
.\dne.cmd skills
.\dne.cmd rebuild
```

`run` creates an absent named model from the included curriculum. Model files are
`models/NAME.brain` by default when launched through `dne.cmd`. Model names use
letters, digits, hyphens, or underscores. The model filename is an identifier;
its conversational name is learned separately.

`--models DIRECTORY` selects another registry. `DNE_MODELS` provides the environment
default. `--curriculum DIRECTORY` selects custom teaching files. A previously taught
model can run without those files; creating a new model needs teaching material.

Normal launches skip compilation. Missing application artifacts trigger the source
build once, and `rebuild` explicitly builds current source changes. `build.cmd` is
also available directly. The default server binds to `127.0.0.1:11435`.

`ui` starts the embedded local web workspace on `127.0.0.1:11436` and opens the
browser. It is equivalent to `serve` plus the web interface; see
[WEB_UI.md](WEB_UI.md). Running `ui` again when the server is already up reopens
the existing page instead of failing.

`--learn-web` enables one bounded source-acquisition attempt for a knowledge gap
identified by native inference state. This works with `run`, `agent`, `ui`, and
`serve`. The default search provider is Wikipedia reference search; a configured
`BRAVE_SEARCH_API_KEY` selects broader Brave web search. Only claims supported by
at least two different site domains are retained, and detected disagreements are
withheld. Learned passages and controlled-premise provenance stay in the model
file. API responses include `learned_from_web` and `learning_error` metadata.

## Generate a response

First create or run the model, then start the server:

```powershell
.\dne.cmd create don
.\dne.cmd serve
```

From another PowerShell window:

```powershell
$body = @{
    model = "don"
    prompt = "what is 23 * (17 - 5)?"
    stream = $false
} | ConvertTo-Json
Invoke-RestMethod -Uri "http://127.0.0.1:11435/api/generate" -Method Post -ContentType "application/json" -Body $body
```

The response includes `model`, `created_at`, `response`, and `done`.

## Chat

```powershell
$body = @{
    model = "don"
    messages = @(
        @{ role = "user"; content = "what is a solar eclipse?" },
        @{ role = "assistant"; content = "A previous answer." },
        @{ role = "user"; content = "explain" }
    )
    stream = $false
    options = @{ max_chars = 8192 }
} | ConvertTo-Json -Depth 5
Invoke-RestMethod -Uri "http://127.0.0.1:11435/api/chat" -Method Post -ContentType "application/json" -Body $body
```

The response has `message: { role: "assistant", content: "..." }`. The current
engine uses the preceding user message as topic context for a short follow-up,
not a full transformer-style history window. `context_used` indicates that an
earlier user topic was supplied. Each loaded model also retains its latest topic
and live identity learning in its brain state. Model state is shared by requests
to that named model.

## Supported endpoints

| Method | Endpoint | Input |
| --- | --- | --- |
| GET | `/api/version` | None |
| GET | `/api/tags` | None |
| POST | `/api/show` | `{ "model": "don" }` |
| POST | `/api/generate` | `model`, `prompt`, optional `stream`, `options` |
| POST | `/api/chat` | `model`, `messages`, optional `stream`, `options` |
| GET | `/api/skills` | None |
| POST | `/api/skills/execute` | `skill`, `arguments`, optional `model` |
| POST | `/api/agent/tasks` | `model`, explicit `steps` |

The supported generation option is `max_chars`, an integer from 0 to 65,536.
The default is 8,192 characters. Unsupported options are rejected explicitly;
language-model options such as temperature or token prediction counts are not
implemented by this runtime. System-prompt instructions are not supported.

Streaming defaults to true. It returns newline-delimited JSON chunks with a final
`done: true` record. Inference completes before chunks are transported, so this is
response chunking rather than incremental token generation. Use `stream: false`
for one JSON response. Missing models return a JSON error with status 404;
invalid requests return status 400.

Models are loaded on demand and retained in process. Calls to one model are
serialized, protecting its mutable native state. Saving is silent, and native
state stays in the same model file. Stop the server with Ctrl+C.

See [SKILLS_API.md](SKILLS_API.md) for tools and search configuration and
[CODING_INTERFACE.md](CODING_INTERFACE.md) for workspace and code-synthesis
operations. Workspace tools in the host require `serve --workspace DIRECTORY`.
