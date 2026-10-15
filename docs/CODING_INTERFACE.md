# .NET coding workspace

## Start

```powershell
.\dne.cmd rebuild
.\dne.cmd agent don --workspace "." --learn-web
```

The terminal shows the model and workspace, chat replies, tool invocations,
results, diffs, and workflow progress. The native engine, model state, and interface
are separate layers. This is a functional coding-tool interface around the
experimental brain, not a frontier coding model.

C# is the only coding-language training and synthesis target. The native C++
implementation is separate from that curriculum. `--learn-web` enables live
knowledge acquisition for unsupported questions; `/learn-web topic` invokes it
explicitly. Search snippets alone are not used as learned answers: readable source
passages are retained with citations, and only the supported controlled grammar
can add source-attributed conceptual premises.

```text
/files *.cs
/read DynamicNeuralEngine.Trainer/Program.cs
/search TrainDialogue
/web C# checked arithmetic documentation
/learn-web C# asynchronous programming
/skills
/model
/tool workspace.run {"executable":"dotnet","arguments":["--version"]}
/tool workspace.replace {"path":"Example.cs","old_text":"oldValue","new_text":"newValue"}
/exit
```

Paths stay within the selected workspace. Listings skip generated folders; text
reads, diffs, searches, and process output are bounded. `read` returns a hash that
can be supplied as `expected_hash` when editing. Replacement must match exactly
one occurrence. Commands use an executable and argument array, not an implicitly
evaluated shell string. Results report actual exit codes.

## Code synthesis from examples

Paste this as one line after `/synthesize `:

```json
{
  "name": "TwicePlusOne",
  "variables": ["x"],
  "examples": [
    {"inputs":[1],"output":3},
    {"inputs":[2],"output":5},
    {"inputs":[3],"output":7}
  ],
  "validation": [
    {"inputs":[10],"output":21},
    {"inputs":[-4],"output":-7}
  ],
  "path": "Learned.cs"
}
```

The skill searches expressions over one or two integer variables, constants
`-1`, `0`, `1`, `2`, and operators `+`, `-`, `*`, `/`, `%`. It explores at most
20,000 candidates and trees up to seven nodes, using checked 64-bit arithmetic.
It does not synthesize loops, arbitrary APIs, or entire applications.

Training examples guide search. The first training-fit hypothesis is checked on
separate validation cases; a failed validation prevents learning. It is then
compiled in an isolated temporary .NET 8 project and executed against the cases.
A successful result can be written to a new source file and retained in the same
brain as a code artifact. Ask `learned code TwicePlusOne` to recall it. Code-fenced
source is preserved verbatim.

Finite examples are not a proof of correctness. The grammar and compiler harness
are supplied mechanisms; finding an expression is the learned part. This is a
bounded program-discovery demonstration, not general coding intelligence.

## Tool workflows

`/task JSON` accepts explicit skill steps. Later arguments can reference an earlier
skill result with `$ref`:

```json
{
  "steps": [
    {"skill":"workspace.read","arguments":{"path":"Example.cs"}},
    {
      "skill":"workspace.replace",
      "arguments":{
        "path":"Example.cs",
        "old_text":"oldValue",
        "new_text":"newValue",
        "expected_hash":{"$ref":"steps.0.data.sha256"}
      }
    },
    {"skill":"workspace.run","arguments":{"executable":"dotnet","arguments":["build"]}}
  ]
}
```

Up to sixteen steps execute with progress and stop on failure. `steps.N` references
that step's skill result. Earlier successful edits are not rolled back. A plan is
supplied by the user/client; the brain does not autonomously invent reliable
general coding plans.

The same workflow is available through `POST /api/agent/tasks` with a `model`
field. Start the host with `--workspace DIRECTORY`. Single tools use
`POST /api/skills/execute`; metadata comes from `GET /api/skills`.

## Current gap to the research goal

The interface supports inspect/edit/run/test work. The brain adds controlled
relation inference, a toy learned policy, and bounded example-driven arithmetic
programs. Arbitrary natural-language coding, broad semantic understanding,
autonomous web research, and OpenCode-equivalent model capability remain
unimplemented. These require additional research and training; tools and code
vocabulary alone do not establish them.
