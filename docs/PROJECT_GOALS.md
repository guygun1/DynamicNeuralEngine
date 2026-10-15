# Project goal and acceptance criteria

## Direction

Build an efficient, continually learning artificial intelligence system that forms
useful representations, connects knowledge, reasons about unfamiliar situations,
and improves through tested experience. Dynamic structural plasticity is a core
research direction. General intelligence is the long-term goal, not a property
established by the current implementation.

## Requirements

- Learn reusable concepts, relationships, and useful behavior rather than only
  associating whole questions with whole answers.
- Create and strengthen meaningful neural representations while reusing existing
  ones. Unknown queries, empty input, and failed guesses should not cause arbitrary
  brain growth.
- Learn English vocabulary, meanings, relationships, and context. Support useful
  explanations and combinations of ideas, including unfamiliar question wording.
- Express uncertainty using learned language when supporting knowledge is absent.
- Learn from explicit corrections and from observable simulation outcomes. Test
  hypotheses against independent evidence rather than accepting generated text
  as truth.
- Keep each AI's learned vocabulary, graph, memories, conceptual facts, policies,
  and retained context in one compact brain file.
- Keep startup and local model management simple. Add capabilities through a
  modular skill API; text is the active modality, with other modalities possible
  later through separate adapters.
- Keep all work local unless the user explicitly requests GitHub updates.

## Evidence of progress

1. Teach premises and ask a conclusion that was never supplied as an answer.
2. Recombine separately learned topics and resolve context-dependent references.
3. Measure behavior before and after experience in a defined environment.
4. Reload the brain and verify that the improvement persists.
5. Measure storage, startup, and representation growth alongside correctness.
6. Include failure cases and held-out tests; successful recall is not a proxy for
   general comprehension.

## Distinctions to preserve

Stored paragraphs are reference material. Recalling or concatenating them is not,
by itself, conceptual understanding. A larger graph is not evidence of intelligence.

The current controlled-English inference grammar, logical propagation rules,
arithmetic evaluator, and sandbox environment are provided mechanisms. Learned
facts and action values use them, but their existence does not show that the system
discovered these mechanisms on its own. Skill endpoints likewise make tools
available; autonomous selection and use of tools must be learned and evaluated
separately.

Do not claim AGI, human-like inner speech, consciousness, general fluency, or general
autonomous learning on the strength of a toy environment or a small teaching pack.
Keep the research goal distinct from concrete verified capabilities.

The .NET coding interface is intended to grow toward an OpenCode-like workflow:
workspace inspection, precise edits, web/tool use, executable verification, and
retained learning. The current program synthesizer covers bounded arithmetic
functions; general natural-language coding and autonomous planning are outstanding
requirements, not completed features.

The coding-learning target is C# only. Use existing knowledge first, then learn
from observable outcomes or attributed web sources when needed. Retain reusable
facts and verified programs in the same brain; source quotations and supplied
logical rules still must not be mislabelled as general understanding.
