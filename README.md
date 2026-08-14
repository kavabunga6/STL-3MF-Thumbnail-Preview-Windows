# Explorer 3D Thumbnails

Native 3D-model thumbnails directly in Windows 10/11 File Explorer.

[Download the latest installer](https://github.com/kavabunga6/Explorer3DThumbnails/releases/latest)

Explorer 3D Thumbnails is a lightweight x64 `IThumbnailProvider`. It renders supported mesh and 3D-printing files locally and returns a static bitmap to Explorer's thumbnail cache. It does not open a preview window and never starts SOLIDWORKS while browsing folders.

## Supported formats

Rendered directly:

- STL — binary and ASCII;
- 3MF;
- OBJ;
- PLY — ASCII, little-endian and big-endian;
- AMF;
- OFF;
- G-code (`.gcode`, `.gco`) toolpaths.

SOLIDWORKS and eDrawings:

- `.sldprt`, `.sldasm`, `.slddrw` and document templates;
- `.eprt`, `.easm`, `.edrw`.

The installer preserves an existing SOLIDWORKS/eDrawings thumbnail provider. If none exists, it attempts to use the preview image embedded in the compound document without launching SOLIDWORKS.

Neutral and third-party CAD extensions such as STEP, IGES, Parasolid, ACIS, IFC, JT, CATIA, Inventor, Creo/NX, Solid Edge, DXF and DWG can be selected in the installer. Existing lightweight system/CAD thumbnail providers are preserved; this project does not launch a full CAD application for each file in a folder.

## Installation

1. Download `Explorer3DThumbnails-Setup-1.2.3.exe` from the [latest release](https://github.com/kavabunga6/Explorer3DThumbnails/releases/latest).
2. Select the extensions to handle.
3. Confirm the Windows UAC prompt.
4. Use Large icons or Extra large icons in File Explorer.

The installer is currently unsigned, so Microsoft Defender SmartScreen may display an unknown-publisher warning.

Requirements:

- Windows 10 or 11 x64;
- [.NET 6 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/6.0).

## Performance and safety

- Explorer initializes the handler through `IInitializeWithStream`, keeping it compatible with out-of-process thumbnail isolation.
- Rendering is capped at 30,000 triangles.
- Large binary STL files are sampled while streaming instead of being fully loaded into memory.
- ASCII STL uses bounded reservoir sampling.
- Non-STL inputs are limited to 64 MB; decompressed 3MF model XML is limited to 16 MB.
- Output is capped at 1024×1024 pixels.
- Explorer caches the generated bitmap.
- No network requests, OpenGL/DirectX dependencies, NuGet packages or SOLIDWORKS Automation are used at runtime.

An 83.6 MB STL used during development produced a 256×256 thumbnail in about 0.2 seconds after warm-up.

## Build from source

Requirements: Windows x64 and .NET 6 SDK.

```powershell
.\scripts\build.ps1
.\scripts\build-installer.ps1
```

The installer is written to `artifacts\Explorer3DThumbnails-Setup-1.2.3.exe`.

Run the smoke tests:

```powershell
dotnet build Explorer3DPreview.sln -c Release -p:Platform=x64
dotnet exec .\Explorer3DPreview.Tests\bin\x64\Release\net6.0-windows\Explorer3DPreview.Tests.dll
```

Tests cover the mesh readers, stream-based Shell initialization, `HBITMAP` generation, embedded PNG decoding and thumbnail size/time limits.

## Uninstall

Use Windows Installed apps, or run:

```powershell
& "$env:ProgramFiles\Explorer3DPreview\uninstall.ps1"
```

Previous thumbnail associations are restored during uninstall.

## Русский

Explorer 3D Thumbnails показывает миниатюры STL, 3MF, OBJ, PLY, AMF, OFF и G-code прямо в списке файлов Проводника Windows. Обработчик работает через системный `IThumbnailProvider`, не открывает отдельное окно и не запускает SOLIDWORKS при просмотре папки.

Скачайте EXE из раздела [Releases](https://github.com/kavabunga6/Explorer3DThumbnails/releases/latest), выберите расширения и подтвердите UAC. После установки включите в Проводнике крупные или огромные значки.

## License

[MIT](LICENSE)
