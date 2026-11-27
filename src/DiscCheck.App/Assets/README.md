# DiscCheck.App assets

- `Icons/disccheck.*.svg`: DiscCheck_Icon_Pack (5 icons, 24x24 grid, `currentColor`). Exposed as Avalonia resources in
  `DiscCheckIcons.axaml` under `DiscCheck.Icon.<key>`. The same geometries are also registered as
  `Das.Icon.<key>` because `DasIcon` (DAS.Design) only looks up `Das.Icon.*`; this keeps `DAS.Design` free of
  `disccheck.*` keys (Requirement 34.2).
- `Icons/disccheck.ico`: application icon (executable, window, taskbar). **Generated, do not edit.**
- `generate-icon.cs`: reproducible generator (.NET file-based app, SkiaSharp). Reads the DAS_Logo monogram
  (`das-mark-color.svg`) and the check of `das.status.ok.svg` straight from `src/DAS.Design/` (the logo is never copied),
  and draws the `#0B7A75` badge with a white check at the bottom right (40% of the side, 50% for 16 and 24 px).
  Sizes: 16, 24, 32, 48, 64, 128, 256.

Regenerate from anywhere inside the repository (needs the .NET 11 SDK and NuGet access the first time):

```powershell
dotnet run src/DiscCheck.App/Assets/generate-icon.cs
```

The script is excluded from the `DiscCheck.App` compilation.
