# Codex App-Server Adapter Boundary

Codex app-server bootstrap/autostart exists separately. Future
event-to-contract adapter work should stay a thin shell over existing
contracts.

## Rule

Do not create a second control stack. Adapter should translate Codex app-server
events onto the same `WindowsOperator.Core` contracts already used by REST and
MCP.

## Suggested shape

1. Receive Codex app-server event.
2. Map event payload onto existing request contract such as `UiQuery`, `UiaClickRequest`, `UiaTypeRequest`, or `HotkeyRequest`.
3. Call `IOperatorFacade`.
4. Return typed success or `OperatorError`.

## Non-goals

- No duplicate automation backend
- No duplicate capture pipeline
- No direct Codex-only action types that bypass shared contracts
