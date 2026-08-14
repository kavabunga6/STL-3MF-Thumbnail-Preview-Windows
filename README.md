# STL, 3MF & STEP Thumbnail Preview for Windows

Fast STL, 3MF, STEP, OBJ, PLY, G-code and SOLIDWORKS thumbnail previews directly in Windows 10/11 File Explorer.

[Download the latest installer](https://github.com/kavabunga6/STL-3MF-Thumbnail-Preview-Windows/releases/latest)

STL, 3MF & STEP Thumbnail Preview for Windows is a lightweight x64 `IThumbnailProvider`. It renders supported mesh and 3D-printing files locally and returns a static bitmap to Explorer's thumbnail cache. It does not open a preview window and never starts SOLIDWORKS while browsing folders.

## Supported formats

Rendered directly:

- STL — binary and ASCII;
- 3MF;
- OBJ;
- PLY — ASCII, little-endian and big-endian;
- AMF;
- OFF;
- G-code (`.gcode`, `.gco`) toolpaths.
- STEP (`.step`, `.stp`) — native AP242 tessellation when present, otherwise a lightweight B-rep wireframe.

STEP support is intentionally lightweight and dependency-free. It produces solid thumbnails for files containing
`TRIANGULATED_FACE` / `TRIANGULATED_SURFACE_SET`, reads faceted loops, and draws lines, circles, ellipses, polylines
and B-spline edges from ordinary `ADVANCED_BREP` files. It does not evaluate or trim full CAD surfaces, so complex
assemblies can appear as a partial wireframe.

SOLIDWORKS and eDrawings:

- `.sldprt`, `.sldasm`, `.slddrw` and document templates;
- `.eprt`, `.easm`, `.edrw`.

The installer preserves an existing SOLIDWORKS/eDrawings thumbnail provider. If none exists, it attempts to use the preview image embedded in the compound document without launching SOLIDWORKS.

## Installation

1. Download `STL-3MF-Thumbnail-Preview-Windows-1.2.5.exe` from the [latest release](https://github.com/kavabunga6/STL-3MF-Thumbnail-Preview-Windows/releases/latest).
2. Select the extensions to handle.
3. Confirm the Windows UAC prompt.
4. The installer safely refreshes the dedicated thumbnail-cache COM surrogate without closing File Explorer.
5. Restart Windows only if thumbnails do not appear immediately; the cache will be rebuilt during startup.
6. Use Large icons or Extra large icons in File Explorer.

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
- STEP parsing is capped at 300,000 entities, 200,000 triangles and 100,000 wireframe segments.
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

The installer is written to `artifacts\STL-3MF-Thumbnail-Preview-Windows-1.2.5.exe`.

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

STL, 3MF & STEP Thumbnail Preview for Windows показывает миниатюры STL, 3MF, STEP, OBJ, PLY, AMF, OFF и G-code прямо в списке файлов Проводника Windows. STEP с готовой триангуляцией отображается как объёмная модель, а обычный B-rep — как лёгкий каркас без внешнего CAD-ядра. Обработчик работает через системный `IThumbnailProvider`, не открывает отдельное окно и не запускает SOLIDWORKS при просмотре папки.

Скачайте EXE из раздела [Releases](https://github.com/kavabunga6/STL-3MF-Thumbnail-Preview-Windows/releases/latest), выберите расширения и подтвердите UAC. Установщик безопасно обновит отдельный процесс кэша миниатюр, не закрывая Проводник. Перезагрузите Windows только если миниатюры не появились сразу. После установки включите в Проводнике крупные или огромные значки.

## License

[MIT](LICENSE)
