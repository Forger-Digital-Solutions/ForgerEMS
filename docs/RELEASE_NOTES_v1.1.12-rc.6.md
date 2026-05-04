# ForgerEMS v1.1.12-rc.6 release notes

## Kyra

- **Unified orchestration**: Kyra continues to route through `KyraOrchestrator`; online engines act as **assist**, not separate branded assistants.
- **Local truth first**: System Intelligence / USB / toolkit facts remain authoritative; online wording is sanitized and can be discarded when it conflicts with local facts.
- **Persistent memory**: Conversation memory keeps redacted rolling context (goals, device/USB hints, toolkit/system issues, last answer, rolling summary) for better follow-ups.
- **Honest live data**: Weather, news, prices, stocks, crypto, sports, time-sensitive legal/software “latest” questions without a configured live tool receive the standard *live data tools not enabled* response—no fabricated numbers.
- **USB builder safety**: The Windows OS drive message for blocked targets is unified to: *ForgerEMS blocks the Windows OS drive from USB build actions to prevent wiping the machine.* Kyra refuses bypass instructions for ForgerEMS safety blocks.
- **Instrumentation**: Provider attempts record latency and outcomes into `KyraProviderResult` via `KyraProviderInstrumentedCall`; `kyra.log` includes richer routing lines (still redacted).
- **Response shaping**: `KyraCopilotResponseBuilder` centralizes Kyra-facing labels (local mode, hybrid, online assist, live tools unavailable, enhanced with online assist).

## Known limitations

- Live weather, news, market prices, and similar feeds require future live-tool wiring and operator configuration in Kyra Advanced.
- Some provider capability routing may still expand in later RCs.
- **Manual WPF QA** using `docs/KYRA-MANUAL-QA-v1.1.12-rc.6.md` remains required before a public beta call.

## Validation

- Solution builds and tests pass under `Release` on this RC branch.
- Installer produced: `dist/installer/ForgerEMS-Setup-v1.1.12-rc.6.exe` (see build script output for your machine).
