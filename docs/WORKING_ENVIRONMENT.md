# Working environment and verification

Inspected locally on October 15, 2026.

## Toolchain

| Component | Observed setup |
| --- | --- |
| OS / shell | Windows x64, Windows PowerShell 5.1 |
| Native code | C++20 |
| Compiler | LLVM-MinGW Clang 22.1.8, x86_64-w64-windows-gnu |
| CMake on PATH | 4.4.3 |
| Build generator | CLion 2026.2.3's bundled Ninja |
| Native build | `cmake-build-debug`, Debug |
| Managed target | `net8.0`, x64 |
| Selected SDK | .NET 10.0.401, no `global.json` pin |
| Installed SDKs | 6.0.136, 8.0.425, 10.0.401 |
| Installed .NET / ASP.NET Core 8 runtime | 8.0.31 |
| Third-party managed packages | None |

The HTTP host uses the installed ASP.NET Core shared framework, not a downloaded
server package. No external compression library or pretrained language model is
required. Ninja is stored as an absolute path in the existing CMake cache even
though it is not directly on PATH. `cmake --build` uses that configuration.

## Build layout

```text
C++ sources -> cmake-build-debug/native/DynamicNeuralEngine.dll
                          |
                          +-> managed build copies it to the selected app output

build.cmd -> cmake-build-debug/app/
run.cmd / dne.cmd -> launch that output immediately when it is present
```

Manual commands:

```powershell
cmake --build "cmake-build-debug" --parallel 4
dotnet build "DynamicNeuralEngine.Trainer/DynamicNeuralEngine.Trainer.csproj"
```

For another native build directory, pass
`-p:NativeLibraryPath="C:/full/path/to/DynamicNeuralEngine.dll"` to the managed
build. The project reports an actionable error when the expected native artifact
is missing. Use the same architecture for the native DLL and managed process.

An earlier managed build encountered a running trainer locking its normal output.
Verification therefore also used separate output/artifacts directories under
`C:/Users/Guy/AppData/Local/Temp/opencode`. Processes were not killed to release
the user's session. The launcher uses its own application output directory.

## Findings corrected

- CMake output variables had been assigned after `add_library`, so they did not
  reliably initialize that existing target's output property. Explicit target
  properties now select one native artifact, which the C# build copies.
- The old curriculum trainer converted characters into floats between zero and
  one. The numeric `Step` method interpreted those as spaces. It now uses token
  text/dialogue training directly.
- Chat had a native 120-character cap and a fixed 512-character managed buffer;
  its C# `maxChars` parameter was ignored. The capped chat export is now explicit,
  buffers match the requested budget, and ordinary replies allow 8,192 characters.
- The former matcher selected the best available answer even when only generic
  question words matched. Topic coverage, ambiguity rejection, and learned
  uncertainty are now used. Question focus is weighted separately from shared
  passage context.
- Shared `is` and `am` connections were globally depressed during renaming.
  Identity corrections now stay in their question contexts.
- Explicit non-language neurons could shift subsequent vocabulary IDs. Vocabulary
  storage now preserves actual graph IDs, including reserved non-word positions.
- Updated teaching files were skipped when an older brain already existed.
  A curriculum fingerprint inside the brain now triggers updates once.
- Repeated launches rebuilt the program unnecessarily. The launcher now builds
  missing artifacts once and provides an explicit `rebuild` command.
- `UseAppHost=false` removes the generated apphost that caused the reported
  executable-copy lock in `dotnet run`. The cached launcher runs the managed DLL.

## Persistence

Current saves use a DNC1 lossless packet envelope containing AGI7 state. The
payload includes the graph, vocabulary, context-specific episodes and original
formatted text, curriculum fingerprint, latest topic, typed facts, and source provenance. Float
weights are retained exactly. A checksum detects payload corruption, and a complete
temporary write precedes replacement of an existing file.

AGI2 through AGI6 files are accepted. Independent binary fixtures test the older
schemas. Not every transient simulator register or unfinished character accumulator
is included, so the format is a learned-state snapshot rather than a complete
execution image.

## Verification performed

- Native build and isolated managed build succeeded with no managed warnings.
- All integration checks passed for training, changed uncertainty wording,
  unfamiliar words, new arithmetic, separate identities, long formatted responses,
  word-node IDs, noise/duplicate prevention, legacy formats, and persistence.
- Localhost HTTP checks passed for model listing, generation, chat, length limits,
  NDJSON framing, and structured errors.
- The exact reported misspelled eclipse question, `explain` follow-up, misspelled
  essay request, and bare `food` topic passed regression checks and a real launcher
  session. Composition also worked for a newly taught fictional topic.
- The measured compact brain was 444,766 bytes versus a 1,160,095-byte raw state,
  roughly 62% smaller. Corrupt compact payloads were rejected.
- A scripted ordinary chat session confirmed that per-message save confirmations
  were absent. Learned responses persisted without requiring source lesson files.
- The pack currently has 713 dialogue pairs, 96 sentences, 21 reasoning premises,
  and two additional initial-identity lessons. A fresh graph has 3,076 word/phrase
  and structural representations after this pack.
- Untaught type conclusions, inherited properties, and containment paths passed
  with supporting premises, including cycles and unsupported reverse implications.
- A seeded sandbox policy improved from 0/100 untrained greedy successes to
  100/100 after 300 episodes; the random-action baseline was 63/100. Reloading kept
  the policy, and another 30 episodes allocated no additional representations.
- The skill API and an injected search provider passed checks without contacting
  a live search service or using credentials.
- Workspace read/search/edit, tool-result references, failure-stop workflows, and
  a scripted coding interface passed. A synthesized integer function compiled
  with the installed SDK and passed separate positive/negative-input cases.
  Failed validation did not teach a code artifact.
- Source-learning checks passed for evidence-state routing, topic-based search,
  read/learn/reuse, citation-only URL storage, source-attributed untaught deductions,
  and persistence. A real Wikipedia test acquired a quokka reference and answered
  the repeat locally. The live check exposed an ambiguous search result for the
  full question, motivating topic-based querying and exact-title preference.

These checks establish concrete program behaviors, not AGI, general fluency, or
human-like thinking. More storage-efficient state does not itself create better
reasoning.

## Build references

- [CMake: CMAKE_RUNTIME_OUTPUT_DIRECTORY](https://cmake.org/cmake/help/latest/variable/CMAKE_RUNTIME_OUTPUT_DIRECTORY.html)
- [Microsoft: dotnet build and artifacts paths](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-build)
