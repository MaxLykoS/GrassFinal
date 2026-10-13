# Codex project instructions

## Excluded directory

- Do not scan, enumerate, search, read, or modify `Library/` or any of its contents (`D:\UnityProject\GrassFinal\Library`).
- Exclude `Library/` from all recursive project searches and file listings. For ripgrep, include `-g '!Library/**'`, including when using `--hidden` or `--no-ignore`.
- Only access this directory if the user explicitly asks to do so in a later request.
