# TexInspect DDS Encoder/Decoder (VB.NET)
A lightweight, high-quality DDS encoder and decoder. TexInspect is a zero-dependency application for handling LDR DirectDraw Surfaces.

![](https://github.com/WalkerMx/DemoImages/blob/032c8a250d69e3aefb59c09a61cd6d7de76c1e6f/TexInspect/v1.3.1.PNG)

## Features
* **Tiny Footprint:** Under 128KB, portable even if you use floppy disks.
* **Compatibility:** Only requires .NET 4.7.2, no extra redistributables.  Compatible with Windows 7 SP1 - 11.
* **Full LDR Support:** Supports encoding and decoding BC1-5, Legacy DXT1-5, ATI1, ATI2, and BC7.
* **MipMaps:** Automated chain generation using Catmull-Rom downsampling.
* **CubeMaps:** Automated detection, encoding, and decoding CubeMaps with full 3D preview (GUI only).
* **Normal Maps:** Specialized alternative encoders for Normal Maps using DXT5n or BC7n.
* **Quality Analysis:** Supports assessing MSE, PSNR, and SSIM.
* **Headers:** Supports Legacy FourCC and modern DX10 (DXGI_Format) extended headers.
* **Reporting:** Capable of reading and validating DDS headers, and generating full reports.

## System Requirements
### Minimum
* **Operating System:** Windows 7 SP1 (x86) with .NET Framework 4.7.2
* **Processor:** Dual-Core CPU (e.g., AMD Athlon 64 X2 / Intel Core2 Duo)
* **Memory:** 2 GB RAM
* **Storage:** >128 KB available space
* **Graphics:** Integrated Graphics or any DirectX 9.0c compatible GPU with 128MB of VRAM
### Recommended
* **Operating System:** Windows 10 / 11 (x64)
* **Processor:** Quad-Core CPU or better
* **Memory:** 8 GB RAM
* **Storage:** Any SSD (SATA or NVMe)
* **Graphics:** Any Dedicated GPU (e.g. GTX 600 Series with 1GB VRAM)
> GPU requirements are recommended minimums for the OS.  TexInspect is GPU-agnostic.

## Performance
Comparison testing was done on a i9-9980HK (2019), strictly on the CPU.  Results are taken from a 50-run average.  While encoding BC7, Texconv was run in 'Quick' mode.  Decoding was benchmarked while exporting to BMP.

### Encoding
| | TexInspect 4K | TexConv 4K | Delta |
| :--- | :--- | :--- | :--- |
| BC7 No Mips | 245.42ms | 23226.3ms | 94.6x Faster |
| BC7 Full Mips | 388.86ms | 32747.8ms | 84.2x Faster |
| BC5 No Mips | 197.32ms | 215.8ms | 1.1x Faster |
| BC5 Full Mips | 310.54ms | 513.9ms | 1.7x Faster |
| BC3 No Mips | 268.72ms | 428.0ms | 1.6x Faster |
| BC3 Full Mips | 415.56ms | 790.5ms | 1.9x Faster |
| BC1 No Mips | 248.40ms | 409.6ms | 1.6x Faster |
| BC1 Full Mips | 352.68ms | 766.7ms | 2.2x Faster |

### Decoding
| | TexInspect 4K | TexConv 4K | Delta |
| :--- | :--- | :--- | :--- |
| BC7 | 606.96ms | 1013.06ms | 1.7x Faster |
| BC5 | 632.42ms | 1289.96ms | 2.0x Faster |
| BC3 | 604.34ms | 839.67ms | 1.4x Faster |
| BC1 | 573.88ms | 873.15ms | 1.5x Faster |

## Quality
Kodak Lossless TrueColor Image Suite Benchmarks (24 Images)
| MSE | TexInspect BC7 | TexConv BC7 | TexInspect BC1 | TexConv BC1 | 
| :--- | :--- | :--- | :--- | :--- |
| **Average** | 3.7526 | 4.6653 | 21.628 | 21.1078 |
| **Worst-Case** | 7.1285 | 10.1183 | 51.9012 | 47.2959 |
> MSE Reference: 0 = Lossless | <2 = Indistinguishable | <20 = Excellent | <65 = Acceptable

| PSNR | TexInspect BC7 | TexConv BC7 | TexInspect BC1 | TexConv BC1 |
| :--- | :--- | :--- | :--- | :--- |
| **Average** | 42.826 dB | 41.9745 dB | 35.2716 dB | 35.3033 dB |
| **Worst-Case** | 39.6008 | 38.0797 dB | 30.979 dB | 31.3826 dB |
> PSNR Reference: 128 = Lossless | >45 dB = Indistinguishable | >35 dB = Excellent | >30 dB = Acceptable

| SSIM | TexInspect BC7 | TexConv BC7 | TexInspect BC1 | TexConv BC1 |
| :--- | :--- | :--- | :--- | :--- |
| **Average** | 0.9915 | 0.9923 | 0.9649 | 0.9608 |
| **Worst-Case** | 0.9839 | 0.9879 | 0.9509 | 0.9476 |
> SSIM Reference: 1.0 = Lossless | >0.98 = Indistinguishable | >0.95 = Excellent | >0.90 = Acceptable

## CLI Usage
```
Usage: TexInspectCLI.exe <input_path> [options]

Options:
  -fmt <format>                  Target Format (e.g., BC7_UNORM, DXT1, ATI2). Default: BC7_UNORM_SRGB
  -m                             Generate Mipmaps
  -ndx, --nodx10                 Force legacy DDS header (Implicitly enabled for DXT/ATI formats)
  -o <path>                      Output file or directory
  -ext <extension>               Output extension for batch decoding (e.g., .jpg, .bmp). Default: .png
  -r, --recursive                Search subdirectories when processing a folder
  -f, --force                    Suppress warnings and overwrite files
  --info                         Show header info for the target file(s) without processing
  -q, --quality <path>           Compare input to reference path and print average MSE, PSNR, & SSIM
  -qv, --qualityverbose <path>   Compare input to reference path and print average & per-channel metrics

Examples:
  Encode PNG to BC7 DDS:
    TexInspectCLI.exe texture.png -fmt BC7_UNORM_SRGB -m

  Encode PNG to legacy DXT5 DDS:
    TexInspectCLI.exe texture.png -fmt DXT5

  Encode a folder of images to DDS into a specific output folder:
    TexInspectCLI.exe "C:\InputImages" -o "C:\OutputDDS" -fmt BC7_UNORM_SRGB -m -f

  Decode a folder of DDS files to JPEGs:
    TexInspectCLI.exe "C:\InputDDS" -o "C:\OutputJPEGs" -ext .jpg -f

  View header data of a specific file:
    TexInspectCLI.exe texture.dds --info

  Generate quality metrics between two files:
    TexInspectCLI.exe texture.dds -q texture.png
```

## Class Usage
If you would like to use TexInspect's engine, usage is simple.  Drop DDS_Common.vb, DDS_Encoder.vb, and/or DDS_Decoder.vb (or compile and add TexInspect.dll) into your project, and call it like this:
### Encoding
```vbnet
'SourcePath, DXGI_Format, MipMaps, LegacySupport
Using Encoder As New DDS_Encoder("input.png", DXGI_Format.DXGI_FORMAT_BC7_UNORM, False, True)
    Encoder.Save("output.dds")
End Using
```
### Decoding
```vbnet
Using Decoder As New DDS_Decoder("input.dds")
    Decoder.Save("output.png", ".png")
End Using
```
