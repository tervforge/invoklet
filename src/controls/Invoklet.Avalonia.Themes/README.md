# Invoklet theme resources

`ThemeKeyCatalog` lists the palette, focus ring, metrics and icon keys that interface themes
can supply. To generate the Markdown reference, run this from the repository root:

```console
dotnet run --file build/GenerateThemeKeyTable.cs
```

`ThemeDefinitionReader` reads JSON with a required `id` and `name`. Use lowercase,
dot-separated identifiers such as `example.theme.ocean` for theme package assets. A theme may declare a
`base` of `dark` or `light` and extend another theme by id. The closest explicit base in the
inheritance chain wins. The default is dark when the chain has no explicit base.

Use color strings in `colors`, numbers in `metrics`, and SVG path data in `icons`. Thickness
also accepts arrays of two values (horizontal and vertical) or four (left, top, right, bottom).
Corner radius accepts an array of four values (top left, top right, bottom right, bottom left).
Unknown keys and invalid overrides appear in the reader's diagnostics. Other valid overrides
stay in the definition, and the resolver uses inherited or built-in values for omitted keys.
The host supplies the two built-in palettes and shared metrics and icons to `ThemeResolver`.
The resolver preserves the source theme id for each inherited editor asset.

Midnight and Warm Light live in `Themes/*.json`. `InvokletTheme` registers these base palettes.
The desktop loads the other six palettes from bundled theme packages and registers them on the application.
Shared defaults live in `Metrics.json`. The application overlays the active theme's metrics,
including the resolved Fluent aliases, so open controls can pick up changes through dynamic resources.
The shared `Icons.axaml` provides the default geometry for each icon. The same active resource
dictionary overlays any icons supplied by the theme.

Each interface theme defines the shared keyboard-focus ring in its theme dictionary:

| Resource | Type | Purpose |
| --- | --- | --- |
| `Invoklet.Focus.BorderBrush` | Brush | Main ring colour |
| `Invoklet.Focus.ContrastBrush` | Brush | Inner edge that remains visible on accent surfaces |
| `Invoklet.Focus.Thickness` | Thickness | Main ring thickness (default 2 DIP) |
| `Invoklet.Focus.ContrastThickness` | Thickness | Inner edge thickness (default 1 DIP) |
| `Invoklet.Focus.CornerRadius` | CornerRadius | Common outer radius (default 4 DIP) |
| `Invoklet.Focus.InnerCornerRadius` | CornerRadius | Inner radius (default 2 DIP) |

`InvokletTheme.axaml` uses one adorner template for shell controls. Resource changes and theme
switches update an already-focused control. Selection and primary-action colours are separate
from focus; text fields keep their caret and validation errors, and code editors keep their
own caret/selection behavior. Keyboard navigation shows the ring; pointer focus does not.

When changing ring colours, keep at least one edge at 3:1 contrast against panel, editor,
selection and primary-button surfaces, including hover and pressed states. Update both radii
when changing the total border thickness to keep the edges concentric.

Tool-panel selectors use `Invoklet.ToolTab.SelectedBackgroundBrush`,
`Invoklet.ToolTab.SelectedForegroundBrush` and `Invoklet.ToolTab.SelectedBorderBrush`.
These are separate from row/document selection accents so tool views can use a softer highlight.
