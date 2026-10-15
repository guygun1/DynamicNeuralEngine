# DynamicNeuralEngine

A small experimental C++ neural graph with an English text adapter, persistent
associative memory, a deterministic arithmetic skill, and a C# local runner.
Learning happens in this engine; no pretrained language model or remote AI service
is used. This project does **not** currently demonstrate AGI or unrestricted
English understanding.

The long-term direction and acceptance criteria are recorded in
[PROJECT_GOALS.md](docs/PROJECT_GOALS.md). The terminal interface provides workspace
tools and bounded code synthesis; it is an OpenCode-inspired interface, not an
OpenCode-equivalent general coding model.

## Run it

Double-click **`Start-DynamicNeuralEngine.bat`** in the project folder. It starts the
local server and opens **http://127.0.0.1:11436/** in your browser. Click **Choose
folder...** to select a development directory, then use the chat, file editor,
Research, C# function, and Activity panels. No browser packages or frontend build
step are required. See [WEB_UI.md](docs/WEB_UI.md).

From PowerShell, the same thing:

```powershell
.\dne.cmd ui current --workspace "." --learn-web
```

The terminal interface is also available:

```powershell
.\dne.cmd agent don --workspace "." --learn-web
```

This creates and teaches `models/don.brain` if needed, then opens chat. Normal
launches use the existing build immediately. The launcher builds missing runtime
artifacts on first use; after changing source code, run `.\dne.cmd rebuild`.
The initial source build uses the existing CLion CMake configuration and installed
CMake/compiler and .NET SDK. The shared .NET 8 / ASP.NET Core 8 runtime is required.

Other commands:

```powershell
.\dne.cmd run don
.\dne.cmd list
.\dne.cmd show don
.\dne.cmd create other-ai
.\dne.cmd create imported --from "path/to/existing.brain"
.\dne.cmd serve
```

`serve` exposes a local Ollama-style API at `http://127.0.0.1:11435`.
See [the local runner guide](docs/LOCAL_RUNNER.md) for requests and supported options.
Use `--models DIRECTORY` or `DNE_MODELS` to select another model folder.

The `agent` command opens the .NET coding workspace interface. See
[CODING_INTERFACE.md](docs/CODING_INTERFACE.md) for file tools, workflows, examples,
and the precise limits of the synthesizer. To enable workspace tools in the HTTP
host, supply `serve --workspace DIRECTORY`.

`.\run.cmd` opens the original trainer-folder brain directly. `--brain path`
selects a particular file; the named-model runner is easier when keeping several
independent AIs.

## What it can currently do

- Recall taught English explanations and distinguish some paraphrases using
  weighted words, adjacent pairs, and question focus.
- Reject unsupported topics or relationships using **learned** uncertainty text.
  There is no built-in English fallback sentence in the C++ generator.
- Recover some unambiguous misspellings against its learned vocabulary.
- Carry the latest topic into follow-ups such as `explain` and `tell me more`.
- Assemble several relevant learned passages for requests such as
  `write an essay about food`. This is extractive composition: the passages are
  learned material, not unrestricted novel prose generation.
- Compute new arithmetic expressions with precedence, parentheses, unary signs,
  decimals, scientific notation, powers, remainder, simple spelled-number phrases,
  and expressions such as `20 percent of 150`. Multi-operation calculations show
  their intermediate arithmetic. Invalid or unsupported expressions abstain;
  undefined real-number operations are reported as undefined.
- Learn explicit dialogue corrections, text sequences, identities, and feedback
  from bounded experiments, then retain that learning after restart.
- Store explicit category, possession, requirement, and containment premises and
  derive untaught conclusions across their relation chains, with supporting facts.
- Learn a bounded control policy from reward feedback in a toy simulation;
  state/action values are saved in the neural graph.
- Expose extensible skills with argument schemas and structured results, including
  workspace operations, a configurable search adapter, and checked code synthesis.
- With `--learn-web`, fill unsupported knowledge requests through search, source
  reading, multi-site claim corroboration, and reuse of retained knowledge. At least
  two different site domains must support each retained statement; otherwise the
  skill keeps a clearly-labelled best-effort answer instead of refusing. Simple
  controlled-English premises can support new source-attributed deductions.
- Perform small imperative requests directly in the web workspace, such as writing
  a previous answer into an open file, and run the project build itself after it
  changes source code. This is a bounded action router, not general task planning.

For example, the reported conversation is now covered by regression checks:

```text
whats a solar clipse       -> a learned solar-eclipse explanation
explain                   -> more learned eclipse passages, using the prior topic
write a eassy about food   -> multiple paragraphs assembled from food lessons
food                      -> a substantive food overview
23 * (17 - 5)             -> 276, with intermediate arithmetic
```

These behaviors are specific tested capabilities, not evidence that arbitrary
English questions will be understood. Short names, ambiguous typos, unfamiliar
topics, complex word problems, and unsupplied knowledge can still fail.

## One compact file per AI

A `.brain` stores its vocabulary, neurons, weighted connections, prompt
associations, taught dialogue episodes, formatting, latest topic, and curriculum
fingerprint. Learned names and experiment corrections live in the same file.
An already taught brain can answer without the source lesson files.

New files use a **lossless DNC1 compact envelope around AGI7 state**. Compression
does not round weights or delete rare knowledge. Saves replace the prior file only
after a complete temporary write; the temporary file is not a second persistent
memory file. AGI2 through AGI6 token brains remain readable by the current engine.
The schema version label is not an intelligence classification. Typed facts and
learned state/action values are retained with the rest of the model.

One measured teaching brain occupied **444,766 bytes**, compared with **1,160,095
bytes** for its uncompressed state: about **62% smaller**. This measures the brain
file, not the shared runtime, development builds, or repository history. Larger
or different lessons can have a different compression ratio.

The saved file is learned state, not an executable or an exact freeze of every
transient simulator field. Use a compatible runtime to load it. The source code
remains modular so other modalities can reuse the core graph.

## Teaching materials

`DynamicNeuralEngine.Trainer/TeachingMaterials/` contains **713 dialogue pairs,
96 original sentences, and 21 explicit reasoning premises**, plus two initial-identity
lessons for new brains.

| Material | Focus | Dialogue pairs |
| --- | --- | ---: |
| `01-conversation` | Greetings, requests, engine capabilities | 55 |
| `02-language` | Grammar, plurals, tense, synonyms, opposites | 60 |
| `03-world` | Elementary world facts | 48 |
| `04-everyday` | Objects, places, time, computing | 50 |
| `05-numbers` | Arithmetic examples and shapes | 50 |
| `06-reading` | Explicit micro-story questions | 20 |
| `09-uncertainty` | Unavailable information and unfamiliar words | 40 |
| `10-history` | Ancient societies, revolutions, world wars, notable people | 92 |
| `11-science` | Physics, biology, Earth science, scientific reasoning | 74 |
| `12-general-knowledge` | Geography, society, economics, everyday reasoning | 72 |
| `13-english-meanings` | Word meanings, usage, sentence roles | 62 |
| `14-food-and-astronomy` | Food, nutrition, eclipses, related passages | 39 |
| `16-programming` | C# target, programming concepts, testing, synthesis | 51 |

`07-english.sentences.txt` and `08-reading.sentences.txt` contain the 96 standalone
sentences. The lessons are original summaries; reference starting points are listed
in [the teaching source notes](docs/TEACHING_SOURCES.md).

`15-reasoning.facts.txt` supplies the 21 controlled-English premises, without
prewritten answers to the multi-step inference tests.

### Lesson format

```text
prompt | response
alternate prompt ~ another prompt | shared response
```

Use literal `\n\n` in a response for a paragraph break. `.sentences.txt` files use
one sentence per line. `.bootstrap.txt` lessons apply only when creating a brain.
Blank lines and `#` comments are ignored. Invalid rows and duplicate prompts are
rejected before training. ASCII English is supported by the current tokenizer and
native bindings.

`<unk>` in a teaching prompt is the unfamiliar-word feature. For example,
`what is <unk>? | i don't know.` teaches an uncertainty response. Changing that
answer and re-teaching changes what is said; tests also verify a completely
different taught uncertainty sentence.

A fingerprint stored **inside the brain** detects updated lesson files on startup.
Changed packs are taught automatically when their source files are available.
Unchanged packs are not retrained on every launch. Initial identity lessons are
excluded from updates, preserving learned names. Re-teaching a pack can replace
custom corrections to questions explicitly defined by that pack.

### Chat commands

- `/teach question | answer` — teach an association.
- `/text sentence` — teach word transitions.
- `/curriculum` — explicitly re-teach the pack.
- `/word photosynthesis` — inspect the learned word's neuron ID.
- `/stats` — inspect neuron and vocabulary counts.
- `/experiment` — test and learn from the bundled reference suite.
- `/experiment path` — use another reference-answer suite.
- `/simulate 300` — learn a toy control policy from 300 episodes.
- `/skills` — list registered skills and argument schemas.
- `/skill name JSON` — invoke a skill explicitly.
- `exit` — leave chat.

Saving is automatic and silent during ordinary chat. Errors are still reported.

## English representations and neuron use

English words reuse stable neuron IDs. Training creates sequential and associative
connections, and dialogue memory supplies context for selecting relevant learned
sequences. Internal state is numeric with English word labels; there is no evidence
of human-like inner speech or consciousness.

Empty/noise-only training creates no word neurons. Repeated words and novelty flags
reuse existing nodes. Questions, typo recovery, generated arithmetic results, and
unverified proposals do not sprout extra word neurons. Explicit core/sensory
neurogenesis remains available for other modalities, and language IDs remain
correct when those nodes exist. Rare learned words are retained rather than being
deleted merely because they have low current activation.

## Propose, test, learn

The bounded experiment loop records initial answers, samples alternative learned
word transitions, checks complete normalized answers against supplied references,
and teaches a successful proposal or a teacher correction. It then checks retention
and saves the result. Failed guesses do not become facts automatically.

`text.experiments.txt` is excluded from initial training. Its final score checks the
same tasks after feedback, not independent generalization. Reference answers remain
teacher-provided; the loop is not a general truth oracle or autonomous researcher.

The separate `/simulate` environment supplies transition rewards rather than
reference sentences. In the seeded corridor task, the untrained greedy policy
scored 0/100, a random baseline 63/100, and the learned policy 100/100 after 300
episodes. It retained that policy after restart. This is a toy reinforcement
learning result, not general autonomous competence.

## Connecting learned facts

For a minimal new example, teach only premises:

```text
/text A glintling is a flutterer.
/text A flutterer is a creature.
/text A creature needs energy.
is a glintling a creature?
does a glintling need energy?
```

The conclusion is computed from the typed relations, not supplied as a dialogue
answer. The engine shows the supporting path. Category implication, inherited
`has`/`needs` properties, and transitive `is in` containment are the current
reasoning mechanisms. They are provided rules over learned premises and a narrow
English grammar, not a learned general theory of language or causation. An
unsupported proposition yields learned uncertainty rather than a guessed negative.

## Skills and coding

[SKILLS_API.md](docs/SKILLS_API.md) explains how to add an `IAgentSkill` and register
it without editing the native core. `brain.query`, `brain.observe`, `web.search`,
workspace tools, and `code.synthesize` use the same contract. Wikipedia reference
search needs no key. `BRAVE_SEARCH_API_KEY` selects broader Brave web search.
The Brave adapter was tested with a mock; Wikipedia source acquisition was also
exercised in the actual application.

The code skill searches a supplied arithmetic-expression grammar, compiles the
result with the installed .NET SDK, checks separate examples, and retains verified
source. The workspace interface also supports reading, precise edits, command
execution, and explicit tool workflows. It does not yet provide autonomous
natural-language planning or arbitrary application coding.

### C# and live knowledge learning

**C# is the only coding-language curriculum and synthesis target.** C++ implements
the native engine; it is not a second coding curriculum.

`--learn-web` enables the provided live acquisition policy: local inference first,
then one bounded search/read/learn attempt when native evidence state indicates
missing knowledge. Search uses the inferred topic rather than question boilerplate.
Only claims supported by at least two different site domains are retained;
single-site details and detected disagreements are withheld. Citations survive
restart, and URLs do not become arbitrary word neurons. On a repeated question,
learned local content is reused without another search. The same knowledge and
premise provenance survive restart in the brain file.

Use `/learn-web topic` in the coding interface or web UI, or the `web.learn` skill,
to request learning explicitly. Broad prose remains reference material; only
statements accepted by the controlled grammar become typed premises. Deductions
using such premises identify their sources. Matching is conservative and handles
controlled statements and close wording, not general semantic fact-checking.
Different domains and source agreement are evidence, not proof of truth. Use
Research or `web.learn` again to re-check an older saved answer.

## Verification

```powershell
.\dne.cmd rebuild
dotnet run --project "tests/DynamicNeuralEngine.Checks.csproj"
```

The dependency-free checks cover arithmetic, uncertainty, English word-node
connections, duplicate/noise prevention, identity isolation, long responses,
question focus, the reported typo/follow-up/essay conversation, compact storage,
legacy brain loading, corruption detection, experiments, and the localhost API.

The optional `--evaluate` command uses 15 held-out prompt wordings and checks for
expected phrases. Phrase hits are not a language-comprehension score. See
[the working environment notes](docs/WORKING_ENVIRONMENT.md) for the verified
toolchain and build layout.
