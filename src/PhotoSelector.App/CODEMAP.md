# PhotoSelector.App Code Map

## Purpose

`PhotoSelector.App` is the current Avalonia GUI shell. Treat it as a replaceable presentation layer: it can preview product workflows and user interaction ideas, but shared product logic belongs in `Core`, `Ai`, or `Config`.

## Important Files

- `Program.cs`: Avalonia application entry point.
- `App.axaml` and `App.axaml.cs`: application setup and styles.
- `MainWindow.axaml` and `MainWindow.axaml.cs`: main window layout and code-behind.
- `ViewModels/MainWindowViewModel.cs`: current UI state, demo workflow, and scan interaction.
- `app.manifest`: Windows app manifest.

## Dependencies

The app should depend on shared projects for real workflows:

- `PhotoSelector.Core` for scanning, project records, persistence, and export.
- `PhotoSelector.Ai` for future rating calls.
- `PhotoSelector.Config` for shared provider config, credential config, and the default catalog database path.

## Boundaries

- Do not place durable business logic in Avalonia views or view models.
- Mock/demo data is acceptable while the GUI is provisional, but production flows should come from shared services.
- Keep UI decisions isolated so the shell can later move to Tauri, WinUI, SwiftUI, MAUI, or another frontend without rewriting core workflow code.
- Avoid making the GUI the only configuration channel; CLI and GUI should share config and credentials.
