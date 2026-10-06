# Bundled document fonts

Source: https://github.com/notofonts/noto-fonts

Pinned source commit: `ffebf8c1ee449e544955a7e813c54f9b73848eac`

Files are copied unchanged from `hinted/ttf/NotoSans` and `hinted/ttf/NotoSansArabic`. Latin regular/bold/italic/bold-italic and Arabic regular/bold are embedded in the renderer's trusted HTML. This avoids depending on fonts installed on the host. Font data is embedded in resulting PDFs as Chromium subsets.

See `OFL.txt` for the SIL Open Font License and notices. Keep this license and the font files when publishing the service.
