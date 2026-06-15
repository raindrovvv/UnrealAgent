# Changelog

All notable changes to UnrealAgent will be documented here.

## [0.1.0-alpha] - 2026-06-15

### Added

- Initial public alpha package for UnrealAgent.
- In-editor Chat UI hosted from the Unreal Editor plugin.
- Local MCP HTTP endpoint and stdio proxy.
- Native editor tools for level actors, assets, Blueprints, UMG, materials, Niagara, Control Rig, Enhanced Input, output logs, and viewport capture.
- Provider integrations for Codex CLI, Claude CLI, Anthropic API, OpenAI-compatible APIs, and DeepSeek-compatible APIs.
- Multimodal image attachment path for vision-capable providers.
- Fast paths for short replies and image-only analysis.
- Lightweight project docs RAG through `docs/RAG_ROUTER.md` when provided by the host project.
- Safety, setup, release checklist, third-party notices, and MIT license documentation.

### Known Limitations

- Alpha quality; APIs and UI behavior may change.
- Primarily tested on Windows and Unreal Engine 5.7-compatible environments.
- Marketplace packaging is not complete.
- Clean empty Unreal project smoke test is still pending.
