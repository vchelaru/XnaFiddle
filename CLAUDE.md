# XnaFiddle Repository Guidelines

## What Is This?

XnaFiddle is a standalone KNI game runner with an in-browser C# editor. It is a Blazor WASM app with a WebGL canvas and Monaco code editor that lets users write, compile, and run XNA/KNI game code directly in the browser. A separate ASP.NET Core Web API (`XnaFiddle.Api`) provides fiddle storage (save/load by slug) backed by PostgreSQL.

See [docs/backend-plan.md](docs/backend-plan.md) for backend architecture decisions, progress, concepts covered, and next steps.

## Agent Workflow

For every task, invoke the appropriate agent from `.claude/agents/` before proceeding. The agent's instructions provide guidelines for how the task should be performed. Before doing any work, announce which agent you are using such as "Invoking coder agent for this task..."

Available agents:
- **coder** — Writing or modifying code and unit tests for new features or bugs
- **qa** — Testing, reviewing changes, and verifying correctness
- **refactoring-specialist** — Refactoring and improving code structure
- **docs-writer** — Writing or updating documentation
- **product-manager** — Breaking down tasks and tracking progress
- **security-auditor** — Security reviews and vulnerability assessments
- **skills-writer** — Creating or updating skill files for Claude Code agent context

Select the agent that best matches the task at hand. For tasks that span multiple concerns (e.g., implement a feature and write tests), invoke the relevant agents in sequence.
