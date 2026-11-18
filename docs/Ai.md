# AI drafting

Caster can draft a Design from the module catalog, draft Terraform for a Directory, and answer in an Ask AI panel with proposed changes to a Directory's files and workspaces. A model does the drafting, on Amazon Bedrock by default or on any OpenAI-compatible endpoint. AI is off by default.

Nothing a model returns is saved on its own. Each feature returns a draft or a list of proposed changes, the planner reviews it, and only what they keep is saved, through the same file, Design and workspace requests (with the same permission and lock rules) as a change made by hand.

## Settings

```json
"Ai": {
  "Enabled": true,
  "Region": "us-east-1",
  "ModelId": "us.anthropic.claude-sonnet-5-5",
  "FallbackModelId": "",
  "MaxTokens": 64000,
  "TimeoutSeconds": 600,
  "Effort": "",
  "Models": {
    "design": { "ModelId": "us.anthropic.claude-haiku-5-5" },
    "terraform": { "Provider": "OpenAI", "BaseUrl": "https://models.example.com/v1", "ApiKey": "...", "ModelId": "gpt-oss-120b", "MaxTokens": 16000 }
  },
  "Prices": {
    "us.anthropic.claude-sonnet-5-5": { "InputPerMillion": 2, "OutputPerMillion": 10, "CacheReadPerMillion": 0.2 }
  }
}
```

As environment variables, the same settings are `Ai__Enabled`, `Ai__ModelId`, `Ai__Models__terraform__Provider` and so on.

- **Enabled** turns the features on. With it off, the draft and Ask AI endpoints answer 409 with "AI drafting is not enabled on this server", which the UI shows in the dialog or panel.
- **Region** and **ModelId** pick the Bedrock model. Credentials come from the default AWS chain (environment variables, a profile, or an instance or pod role), never from Caster's settings.
- **FallbackModelId** is tried once when the model's safety classifiers decline a request. Empty means no retry.
- **MaxTokens** limits one answer. Answers are streamed, so it can be well past what an unstreamed request would finish in. A draft cut off at the limit says it ran out of output tokens.
- **TimeoutSeconds** limits one call.
- **Effort** is how hard a model thinks before it answers: `low`, `medium`, `high`, `xhigh` or `max`. Empty uses the model's own default. Lower is faster and spends fewer tokens. A task's own `Effort` wins. OpenAI-compatible models take `low`, `medium` or `high`, and higher settings are sent as `high`.

## Models per task

`Ai:Models` gives a task its own model where it should not use the default. The keys are the tasks:

| Task | Used for |
| --- | --- |
| `design` | Draft a Design with AI, from the module catalog |
| `terraform` | Draft Terraform with AI, or change a file with AI, from a request in words |
| `brief` | Draft Terraform with AI from a structured scenario brief, as Blueprint sends it |
| `assist` | The Ask AI panel |

A task's settings are `Provider`, `ModelId`, `FallbackModelId`, `Region`, `BaseUrl`, `ApiKey`, `MaxTokens`, `Effort` and `Strict`. Anything left out is the default's.

- **Provider** is `Bedrock` (the default) or `OpenAI`. `OpenAI` is any OpenAI-compatible chat completions endpoint: the OpenAI API, or a vLLM or Ollama server for local models. `BaseUrl` is the endpoint's base, for example `https://api.openai.com/v1`, and Caster adds `chat/completions`. Another provider's models are not Bedrock's, so such a task takes nothing of the default except `Region`, `MaxTokens` and `Effort`.
- A Terraform draft returns every file it changes in full, so a local model needs a `MaxTokens` that its server allows alongside the prompt.
- **Strict** constrains a task's answer to its tool's schema, with Bedrock's strict tool use. It is on by default for `assist` and off for the drafts, whose file-heavy schemas are large. A schema Bedrock finds too large is asked again without it.

### Choosing models

The default model runs every task. A task rarely needs the same model and effort as the others, so it is worth giving each its own:

| Task | Starting point | Why |
| --- | --- | --- |
| `design` | A smaller model (Haiku), `low` or `medium` effort | It only picks catalog modules and fills in their values, against a tight schema. |
| `terraform` | A mid-size model (Sonnet), `medium` or `high` effort | It writes whole files, and a mistake fails the plan. |
| `brief` | Sonnet or a larger model (Opus), `high` effort | It has the largest input and needs real topology reasoning. |
| `assist` | Sonnet, `medium` effort | It is interactive, so speed matters. Raise the effort to fix failed runs. |

These are starting points to try, not measured results. Set `Ai:Prices` and compare tasks and models in the project's AI usage panel, which shows tokens and estimated cost for each task, before committing to a change. Swapping a smaller model into `design` is the safest first experiment.

```json
"Ai": {
  "ModelId": "us.anthropic.claude-sonnet-5-5",
  "Models": {
    "design": { "ModelId": "us.anthropic.claude-haiku-5-5", "Effort": "low" },
    "brief": { "ModelId": "us.anthropic.claude-opus-5-5", "Effort": "high" }
  }
}
```

## What the model sees

- **Design drafts** see the module catalog: each Module with a version, its variables and outputs, its source and the hypervisor it works on.
- **Terraform drafts** see the Directory's files, its parents' files and the enabled Designs. They may change only the Directory's own files, and say in their notes what a read-only file would need.
- **Ask AI** sees the Directory's files and its parents', its workspaces with their Terraform versions and own files (each marked editable or read-only, with the reason), the latest run's plan or apply output when it failed, and the enabled Designs.
- **Provider settings.** The drafts and Ask AI are told the names, never the values, of the provider settings that Caster's runs set in the environment (for example `PROXMOX_VE_ENDPOINT`), so they leave those out of provider blocks and declare no variables for them.
- **Credentials** are never put in a prompt, and a draft is told never to write one into a file.

### Grounding in the inventory

For a caller who holds `ViewInfrastructure`, drafts are also shown the VM templates, networks and datastores of the [inventory](Inventory.md) connections for the draft's hypervisor, compactly and within a budget. The template, network and datastore names a draft sets are then looked up there, and an unknown one is a warning in the review.

The review says when a draft was not checked and why: the inventory is not enabled, it could not be read, or the caller lacks `ViewInfrastructure`. The draft still goes ahead.

## Reviewing a draft

A draft comes back with a summary, notes and warnings. Both kinds of draft are also returned with a topology plan: the segments and the machines, and an attack path when there is an attacker. Crucible serves defensive exercises, purple teaming and plain software testing, so the models are told to build what the request asks for: no attacker or attack path unless the request or the existing files include one, and no perimeter, DMZ or server tiers unless the request describes an organization. The server checks the plan for:

- overlapping or public addresses, and addresses outside their segment,
- reused VLANs,
- machines that bypass segmentation,
- an attacker beside the critical assets, when the plan has an attacker,
- disk beyond what the datastores have free.

Each machine's size is taken from the plan, or from its template's size in the inventory. A machine with neither is left out of the totals, and the review says how many are unsized.

A Design draft may leave a value it cannot know as a placeholder, which starts with `REPLACE_WITH_`. A Design with placeholders can be created, but not enabled: enabling it, or enabling a module in an enabled Design, is refused until each placeholder has a value.

### What Ask AI may change

Ask AI proposes these changes, and the planner keeps or drops each one:

- add a file to the Directory;
- replace a file of the Directory;
- replace a file of one of the Directory's workspaces, which is usually that workspace's variable values (`.tfvars`);
- set a workspace's Terraform version to one Caster has.

It cannot delete or rename a file, change a parent Directory's files, or change a Design.

A workspace's own file can be replaced, never added, and only when a person made it and it is safe to show a model. Ask AI leaves a workspace file alone, and says why, when:

- **Blueprint generates it.** Blueprint writes a `blueprint-<id>.auto.tfvars` for each workspace it makes, and writes it again, so an edit would be overwritten or would fight it.
- **It sets a credential.** A password, secret, token or key set to a literal value.
- **It is too long to show in full.** The model is shown up to 4,000 characters of a workspace file, and cannot replace a file it has not seen whole.

These rules are checked twice: when the change is proposed, and again against the file as it is when the planner applies it, because the apply request comes from a client. A workspace file is saved the way the editor saves it, under the file's lock and with the same "changed since" check as any other file: if someone saved it after the proposal, it is not replaced.

## Usage and cost

Every call to a model is recorded against the project of the Directory it was for, with its task, model and tokens (input, output and cache reads and writes), and whether a safety classifier declined it, which is billed too. `GET api/projects/{id}/ai-usage` returns a project's usage in all and by task, and the UI shows it in the project's side panel.

The estimated cost uses `Ai:Prices`, in US dollars per million tokens, keyed by model or inference profile id. A key matches ignoring punctuation, so an environment variable can price `us.anthropic.claude-sonnet-5-5` as `Ai__Prices__us_anthropic_claude_sonnet_5_5__InputPerMillion`. The cost is returned in millionths of a dollar, from the calls whose models have a price. A model with no price is counted in tokens only and listed as unpriced. `Prices` ships empty.

## Endpoints and permissions

| Endpoint | Does | Needs |
| --- | --- | --- |
| `GET directories/{id}/designs/ai-draft/options` | Whether AI is enabled and which hypervisors runs can build on | `EditProject` |
| `POST directories/{id}/designs/ai-draft` | Draft a Design | `EditProject` |
| `POST directories/{id}/designs/ai-draft/create` | Create a disabled Design from a reviewed draft | `EditProject` |
| `POST directories/{id}/files/ai-draft` | Draft Terraform | `EditProject` |
| `POST directories/{id}/files/ai-draft/brief` | Draft Terraform from a scenario brief | `EditProject` |
| `POST directories/{id}/files/ai-draft/apply` | Save a reviewed Terraform draft | `EditProject`, and `LockFiles` to replace a locked file |
| `POST directories/{id}/ai-assist` | Ask AI: a reply and proposed changes | `EditProject` |
| `POST directories/{id}/ai-assist/apply` | Make the proposed changes the planner kept | `EditProject` |
| `GET projects/{id}/ai-usage` | A project's AI usage and estimated cost | `ViewProject` |

The system-wide permission for each is `EditProjects`, `LockFiles` and `ViewProjects`.

Saving a Terraform draft is all or nothing: every file is saved or none is. A file that is replaced must not have changed since the draft, and must not be held by another user. Applying Ask AI changes checks each change again against the Directory as it is now, and does not replace a file that has changed since the proposal. It reports whether each change was applied, and why not.
