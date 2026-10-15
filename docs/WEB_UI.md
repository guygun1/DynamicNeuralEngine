# Local web workspace

The project ships a small embedded web interface served by the same local process
as the model runtime. It behaves like a chat client with a file editor and a
directory picker; it is not a hosted service and does not call a remote AI. The
most reliable way to start it is to double-click `Start-DynamicNeuralEngine.bat`,
which builds missing runtime files on first use, starts the server, and opens the
browser.

The equivalent command is:

```powershell
.\dne.cmd ui current --workspace "." --learn-web
```

The default address is `http://127.0.0.1:11436/`. Running `ui` again while the
server is up detects the running instance and only reopens the page.

## Panels

- **Chat** uses the real engine. Each reply comes from the selected model's learned
  state; arithmetic is computed, and supported facts are recalled. A footer on a
  reply indicates a purely local answer or one that used multi-site learning.
- **Acting on a request.** Imperative follow-ups are performed instead of being
  researched: for example, with a file open, `write it` writes the previous answer
  into that file, and `write "..." to src/Thing.cs` writes the given text there. When
  the engine changes project source, it runs the build itself and reports the result
  in the Activity panel. This is a bounded action router (it reuses text the model
  already produced); it is not general task planning or code generation.
- **Development directory.** Choose **folder...** for the system folder picker, or
  type a path. All file tools are confined to this directory. The selection is kept
  per browser session; a page reload keeps the last directory, and choosing a
  different directory starts a separate editor context.
- **Files.** The sidebar lists real files in the chosen directory. Selecting one
  opens it in the **Editor** tab. Only text files up to the preview limit are
  editable; larger files open read-only.
- **Editor.** Edits are saved only when you choose **Save** (or press Ctrl+S). A
  save carries the file's hash, so a file changed elsewhere is not overwritten
  blindly. A diff appears in the Activity panel after a successful save.
- **Research.** Runs `web.learn` with a query, an optional recall question, and a
  minimum number of independent sources. See [SKILLS_API.md](SKILLS_API.md) for the
  corroboration rules.
- **C# function.** Runs example-driven `code.synthesize`: provide training cases and
  optional separate validation cases (`inputs => output`, one per line). Generated
  code is compiled and checked on the validation cases before it is retained. C# is
  the only supported synthesis target.
- **Activity.** Shows recent tool calls with their arguments and structured results,
  including failed calls.

## Chat commands

- `/help` — short help.
- `/files [pattern]` — refresh the sidebar listing.
- `/read path` — open a file.
- `/search text` — search files in the directory.
- `/web query` — search without learning.
- `/learn-web topic` — search, read, corroborate, and retain knowledge.
- `/build` — run `dotnet build` in the workspace (the assistant also runs this
  automatically after it changes project code).
- `/test` — run `dotnet test` in the workspace.
- `/skills` — open the skill list.
- `/model` — show the current model description.
- `/tool NAME JSON` — invoke any registered skill.
- `/task JSON` — run an explicit agent workflow.

## Endpoints used

| Method | Endpoint | Purpose |
| --- | --- | --- |
| GET | `/` | The interface page |
| GET | `/assets/app.css`, `/assets/app.js` | Interface assets |
| GET | `/api/ui/config` | Application name, UI version, default workspace, picker availability |
| POST | `/api/ui/workspace` | Register a chosen directory, returning a workspace id |
| POST | `/api/ui/pick-directory` | Open the native folder picker |
| POST | `/api/ui/models` | Create and describe a model |

Chat, generation, skills, and workflows use the same `/api/chat`,
`/api/generate`, `/api/skills/*`, and `/api/agent/tasks` endpoints documented in
[LOCAL_RUNNER.md](LOCAL_RUNNER.md).

## Limits

Conversation history shown in the browser is a convenience stored in the browser,
not the model. It is scoped per model and per chosen directory; it does not change
what the brain has learned. The window's side panels and the tool Activity list are
presentation only. The interface reflects the engine's real limits: it recalls and
recombines taught material, applies its arithmetic and fact-inference code, and can
retain corroborated web knowledge, but it does not perform general natural-language
understanding or unrestricted coding.
