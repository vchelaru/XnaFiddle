Third-Party Notices
===================

XnaFiddle is licensed under the MIT License. See [LICENSE](LICENSE).

This repository bundles third-party assets (fonts, images, etc.) in the
`Examples/` directory for use in built-in example code, and in the
`StandardContent/` directory for built-in assets any fiddle can reference
(see [STANDARD_CONTENT_PLAN.md](STANDARD_CONTENT_PLAN.md)). These assets are
distributed under their own licenses and are **not** covered by the XnaFiddle
MIT License. Unless otherwise noted below, bundled assets retain the license
terms of their original projects.

NuGet package dependencies (Gum, Apos.Shapes, FontStashSharp, MonoGame.Extended,
etc.) are restored at build time and are governed by their respective licenses.

## Explicit Attribution

The following assets require explicit attribution per their license terms:

| Asset | License | Copyright | Source |
|-------|---------|-----------|--------|
| `Examples/FontStashSharp.DroidSans.ttf` | Apache 2.0 | Google | [FontStashSharp/samples/Fonts](https://github.com/FontStashSharp/FontStashSharp/tree/main/samples/Fonts) |
| `StandardContent/DroidSans.ttf` | Apache 2.0 | Google | Same font as `Examples/FontStashSharp.DroidSans.ttf` above, registered as built-in standard content (`std/DroidSans.ttf`) |
