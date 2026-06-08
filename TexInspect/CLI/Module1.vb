Imports System.IO
Imports System.Text

Public Module Module1

    Private BenchTimer As Stopwatch
    Private BenchCounter As Integer

    Private Class CliOptions
        Public InputPath As String = ""
        Public InputPaths(5) As String
        Public OutputPath As String = ""
        Public Format As DXGI_Format = DXGI_Format.DXGI_FORMAT_BC7_UNORM_SRGB
        Public SpecialFlags As DDS_SpecialFlags = 0
        Public GenerateMips As Boolean = False
        Public UseLegacyHeader As Boolean = False
        Public ShowInfo As Boolean = False
        Public ForceOverwrite As Boolean = False
        Public RecursiveSearch As Boolean = False
        Public TargetImgExt As String = ".png"
        Public ReferencePath As String = ""
        Public Verbose As Boolean = False
        Public IsCube As Boolean = False
        Public BenchEnabled As Boolean
    End Class

    Private ReadOnly ImgExts As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {".png", ".jpg", ".jpeg", ".bmp"}
    Private ReadOnly DdsExts As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {".dds"}
    Private ReadOnly LegacyFormats As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {"DXT1", "DXT2", "DXT3", "DXT4", "DXT5", "DXT5n", "ATI1", "ATI2"}

    Private ReadOnly FormatAliases As New Dictionary(Of String, DXGI_Format)(StringComparer.OrdinalIgnoreCase) From {
        {"BC1", DXGI_Format.DXGI_FORMAT_BC1_UNORM_SRGB}, {"BC1 SRGB", DXGI_Format.DXGI_FORMAT_BC1_UNORM_SRGB}, {"DXT1", DXGI_Format.DXGI_FORMAT_BC1_UNORM_SRGB}, {"BC1_UNORM_SRGB", DXGI_Format.DXGI_FORMAT_BC1_UNORM_SRGB},
        {"BC2", DXGI_Format.DXGI_FORMAT_BC2_UNORM_SRGB}, {"BC2 SRGB", DXGI_Format.DXGI_FORMAT_BC2_UNORM_SRGB}, {"DXT2", DXGI_Format.DXGI_FORMAT_BC2_UNORM_SRGB}, {"DXT3", DXGI_Format.DXGI_FORMAT_BC2_UNORM_SRGB},
        {"BC2_UNORM_SRGB", DXGI_Format.DXGI_FORMAT_BC2_UNORM_SRGB}, {"BC3", DXGI_Format.DXGI_FORMAT_BC3_UNORM_SRGB}, {"BC3 SRGB", DXGI_Format.DXGI_FORMAT_BC3_UNORM_SRGB}, {"DXT4", DXGI_Format.DXGI_FORMAT_BC3_UNORM_SRGB},
        {"DXT5", DXGI_Format.DXGI_FORMAT_BC3_UNORM_SRGB}, {"DXT5n", DXGI_Format.DXGI_FORMAT_BC3_UNORM_SRGB}, {"BC3_UNORM_SRGB", DXGI_Format.DXGI_FORMAT_BC3_UNORM_SRGB}, {"BC4", DXGI_Format.DXGI_FORMAT_BC4_UNORM},
        {"BC4 UNORM", DXGI_Format.DXGI_FORMAT_BC4_UNORM}, {"ATI1", DXGI_Format.DXGI_FORMAT_BC4_UNORM}, {"ATI1 (BC4)", DXGI_Format.DXGI_FORMAT_BC4_UNORM}, {"BC4_UNORM", DXGI_Format.DXGI_FORMAT_BC4_UNORM},
        {"BC5", DXGI_Format.DXGI_FORMAT_BC5_UNORM}, {"BC5 UNORM", DXGI_Format.DXGI_FORMAT_BC5_UNORM}, {"ATI2", DXGI_Format.DXGI_FORMAT_BC5_UNORM}, {"ATI2 (BC5)", DXGI_Format.DXGI_FORMAT_BC5_UNORM},
        {"BC5_UNORM", DXGI_Format.DXGI_FORMAT_BC5_UNORM}, {"BC7n", DXGI_Format.DXGI_FORMAT_BC7_UNORM_SRGB}, {"BC7", DXGI_Format.DXGI_FORMAT_BC7_UNORM_SRGB}, {"BC7 SRGB", DXGI_Format.DXGI_FORMAT_BC7_UNORM_SRGB},
        {"BC7_UNORM_SRGB", DXGI_Format.DXGI_FORMAT_BC7_UNORM_SRGB}, {"BGRX", DXGI_Format.DXGI_FORMAT_B8G8R8X8_UNORM_SRGB}, {"BGRX (B8G8R8X8)", DXGI_Format.DXGI_FORMAT_B8G8R8X8_UNORM_SRGB},
        {"B8G8R8X8_UNORM_SRGB", DXGI_Format.DXGI_FORMAT_B8G8R8X8_UNORM_SRGB}, {"BGRA", DXGI_Format.DXGI_FORMAT_B8G8R8A8_UNORM_SRGB}, {"BGRA (B8G8R8A8)", DXGI_Format.DXGI_FORMAT_B8G8R8A8_UNORM_SRGB}, {"B8G8R8A8_UNORM_SRGB", DXGI_Format.DXGI_FORMAT_B8G8R8A8_UNORM_SRGB}}

    <STAThread>
    Sub Main(args As String())
        If args.Length = 0 OrElse args.Contains("-h", StringComparer.OrdinalIgnoreCase) OrElse args.Contains("--help", StringComparer.OrdinalIgnoreCase) Then
            DisplayHelp()
            Return
        End If
        Try
            Dim CliOpts As CliOptions = ParseArguments(args)
            If CliOpts.BenchEnabled Then BenchTimer = Stopwatch.StartNew
            ExecuteCommand(CliOpts)
            If CliOpts.BenchEnabled Then
                BenchTimer.Stop()
                Console.WriteLine($"{vbCrLf}Execution Time: {BenchTimer.ElapsedMilliseconds / 1000D} seconds")
            End If
        Catch ex As Exception
            Console.WriteLine($"[CRITICAL ERROR] {ex.Message}")
        End Try
    End Sub

    Private Function ParseArguments(args As String()) As CliOptions
        Dim Opts As New CliOptions()
        Dim i As Integer = 0
        While i < args.Length
            Select Case args(i).ToLower()
                Case "-f", "--force"
                    Opts.ForceOverwrite = True
                Case "-fmt", "--format"
                    Opts.Format = GetFormatFromString(args(i + 1))
                    Select Case args(i + 1).ToLower
                        Case "dxt2"
                            Opts.SpecialFlags = DDS_SpecialFlags.DDS_DXT2
                        Case "dxt4"
                            Opts.SpecialFlags = DDS_SpecialFlags.DDS_DXT4
                        Case "dxt5n"
                            Opts.SpecialFlags = DDS_SpecialFlags.DDS_DXT5n
                        Case "bc7n"
                            Opts.SpecialFlags = DDS_SpecialFlags.DDS_BC7n
                    End Select
                    If LegacyFormats.Contains(args(i + 1)) Then Opts.UseLegacyHeader = True
                    i += 1
                Case "-m", "--mipmaps"
                    Opts.GenerateMips = True
                Case "-ndx", "--nodx10"
                    Opts.UseLegacyHeader = True
                Case "-o", "--output"
                    Opts.OutputPath = args(i + 1)
                    i += 1
                Case "-r", "--recursive"
                    Opts.RecursiveSearch = True
                Case "-ext", "--ext"
                    If i + 1 < args.Length Then
                        Opts.TargetImgExt = args(i + 1)
                        If Not Opts.TargetImgExt.StartsWith(".") Then Opts.TargetImgExt = "." & Opts.TargetImgExt
                        i += 1
                    End If
                Case "-inf", "--info"
                    Opts.ShowInfo = True
                Case "-t", "--timing"
                    Opts.BenchEnabled = True
                Case "-q", "--quality"
                    If i + 1 < args.Length Then
                        Opts.ReferencePath = args(i + 1)
                        i += 1
                    End If
                Case "-qv", "--qualityverbose"
                    Opts.Verbose = True
                    If i + 1 < args.Length Then
                        Opts.ReferencePath = args(i + 1)
                        i += 1
                    End If
                Case "-px", "--right"
                    Opts.InputPaths(0) = args(i + 1) : Opts.IsCube = True
                Case "-nx", "--left"
                    Opts.InputPaths(1) = args(i + 1) : Opts.IsCube = True
                Case "-py", "--top", "--up"
                    Opts.InputPaths(2) = args(i + 1) : Opts.IsCube = True
                Case "-ny", "--bottom", "--down"
                    Opts.InputPaths(3) = args(i + 1) : Opts.IsCube = True
                Case "-pz", "--front", "--forward"
                    Opts.InputPaths(4) = args(i + 1) : Opts.IsCube = True
                Case "-nz", "--back"
                    Opts.InputPaths(5) = args(i + 1) : Opts.IsCube = True
                Case Else
                    If Not args(i).StartsWith("-") Then Opts.InputPath = args(i)
            End Select
            i += 1
        End While

        Return Opts
    End Function

    Private Sub ExecuteCommand(CliOpts As CliOptions)
        If CliOpts.Verbose OrElse Not String.IsNullOrWhiteSpace(CliOpts.ReferencePath) Then
            ExecuteQualityMode(CliOpts)
            Return
        End If
        If CliOpts.IsCube Then
            For i As Integer = 0 To 5
                If String.IsNullOrWhiteSpace(CliOpts.InputPaths(i)) OrElse Not File.Exists(CliOpts.InputPaths(i)) Then
                    Console.WriteLine($"[ERROR] Missing or invalid file for cubemap face. Make sure all 6 faces (-px, -nx, -py, -ny, -pz, -nz) are provided.")
                    Return
                End If
            Next
            Dim TargetPath As String = CliOpts.OutputPath
            If String.IsNullOrWhiteSpace(TargetPath) Then
                Dim TargetFolder As String = Path.GetDirectoryName(CliOpts.InputPaths(0))
                Dim TargetFileName As String = Path.GetFileNameWithoutExtension(CliOpts.InputPaths(0).Replace("_PX", "").Replace("_px", ""))
                TargetPath = Path.Combine(TargetFolder, TargetFileName)
                Console.WriteLine($"[WARNING] No output path defined. Defaulting to {TargetFileName}")
            End If
            EncodeDDS("", TargetPath, CliOpts)
            Return
        End If

        If Directory.Exists(CliOpts.InputPath) Then
            ProcessBatch(CliOpts)
        ElseIf File.Exists(CliOpts.InputPath) Then
            If CliOpts.ShowInfo Then
                ShowFileInfo(CliOpts.InputPath)
            Else
                ProcessFile(CliOpts.InputPath, CliOpts.OutputPath, CliOpts)
            End If
        Else
            Console.WriteLine($"Error: Path '{CliOpts.InputPath}' not found.")
        End If
    End Sub

    Private Sub ExecuteQualityMode(CliOpts As CliOptions)
        If String.IsNullOrWhiteSpace(CliOpts.ReferencePath) Then
            Console.WriteLine("Error: Missing reference path.")
            Return
        End If
        Dim InputIsFile As Boolean = File.Exists(CliOpts.InputPath)
        Dim InputIsDir As Boolean = Directory.Exists(CliOpts.InputPath)
        Dim RefIsFile As Boolean = File.Exists(CliOpts.ReferencePath)
        Dim RefIsDir As Boolean = Directory.Exists(CliOpts.ReferencePath)
        If (InputIsFile AndAlso Not RefIsFile) OrElse (InputIsDir AndAlso Not RefIsDir) Then
            Console.WriteLine("Error: Type mismatch. Both paths must be either both be files, or directories.")
            Return
        End If
        If InputIsDir Then
            HandleQualityBatch(CliOpts)
        ElseIf InputIsFile Then
            RunQualityMetrics(CliOpts.InputPath, CliOpts.ReferencePath, CliOpts)
        Else
            Console.WriteLine($"Error: Input path '{CliOpts.InputPath}' not found.")
        End If
    End Sub

    Private Sub ProcessBatch(CliOpts As CliOptions)
        If Not CliOpts.ShowInfo AndAlso String.IsNullOrEmpty(CliOpts.OutputPath) AndAlso Not CliOpts.ForceOverwrite Then
            Console.Write("Warning: No output directory defined. Files will be saved in-place. Continue? (Y/N): ")
            If Console.ReadKey().Key <> ConsoleKey.Y Then
                Console.WriteLine(vbCrLf & "Operation cancelled.")
                Return
            End If
            Console.WriteLine()
        End If
        Dim SearchOpts = If(CliOpts.RecursiveSearch, SearchOption.AllDirectories, SearchOption.TopDirectoryOnly)
        For Each FilePath As String In Directory.EnumerateFiles(CliOpts.InputPath, "*.*", SearchOpts)
            Dim FileExt = Path.GetExtension(FilePath)
            Dim TargetPath As String = ""
            If ImgExts.Contains(FileExt) Then
                TargetPath = BuildOutputPath(FilePath, CliOpts.InputPath, CliOpts.OutputPath, ".dds")
            ElseIf DdsExts.Contains(FileExt) Then
                TargetPath = BuildOutputPath(FilePath, CliOpts.InputPath, CliOpts.OutputPath, CliOpts.TargetImgExt)
            Else
                Continue For
            End If
            If CliOpts.ShowInfo Then
                ShowFileInfo(FilePath)
            Else
                ProcessFile(FilePath, TargetPath, CliOpts)
            End If
        Next
    End Sub

    Private Sub ProcessFile(Source As String, Target As String, CliOpts As CliOptions)
        Try
            Dim SourceExt = Path.GetExtension(Source).ToLower()
            Dim TargetExt = Path.GetExtension(Target).ToLower()
            If SourceExt = ".dds" Then
                If TargetExt = ".dds" Then Target = Path.ChangeExtension(Target, ".png")
                DecodeDDS(Source, Target)
            Else
                If TargetExt <> ".dds" Then Target = Path.ChangeExtension(Target, ".dds")
                EncodeDDS(Source, Target, CliOpts)
            End If
        Catch ex As Exception
            Console.WriteLine($"[FAILED] {Path.GetFileName(Source)}: {ex.Message}")
        End Try
    End Sub

    Private Sub DecodeDDS(Source As String, Target As String)
        Using Decoder As New DDS_Decoder(Source)
            If Decoder.IsCubeMap Then
                Dim TargetCubePath As String = Path.Combine(Path.GetDirectoryName(Target), Path.GetFileNameWithoutExtension(Target))
                Console.WriteLine($"[INFO] {Path.GetFileName(Source)} is a CubeMap. Extracting faces...")
                Decoder.SaveCubeMaps(TargetCubePath, Path.GetExtension(Target))
            Else
                Decoder.Save(Target, Path.GetExtension(Target))
            End If
        End Using
        Console.WriteLine($"[SUCCESS] {Path.GetFileName(Source)} -> {Path.GetFileName(Target)}")
    End Sub

    Private Sub EncodeDDS(Source As String, Target As String, CliOpts As CliOptions)
        If CliOpts.IsCube Then
            Using Encoder As New DDS_Encoder(CliOpts.InputPaths, CliOpts.Format, CliOpts.GenerateMips, CliOpts.UseLegacyHeader)
                Encoder.Save(Target)
            End Using
            Console.WriteLine($"[SUCCESS] CubeMap (6 faces) -> {Path.GetFileName(Target)}")
        Else
            Using Encoder As New DDS_Encoder(Source, CliOpts.Format, CliOpts.GenerateMips, CliOpts.UseLegacyHeader, CliOpts.SpecialFlags)
                Encoder.Save(Target)
            End Using
            Console.WriteLine($"[SUCCESS] {Path.GetFileName(Source)} -> {Path.GetFileName(Target)}")
        End If
    End Sub

    Private Sub HandleQualityBatch(CliOpts As CliOptions)
        Dim SearchOpts = If(CliOpts.RecursiveSearch, SearchOption.AllDirectories, SearchOption.TopDirectoryOnly)
        For Each FilePath As String In Directory.EnumerateFiles(CliOpts.InputPath, "*.*", SearchOpts)
            Dim FileExt = Path.GetExtension(FilePath)
            If Not (ImgExts.Contains(FileExt) OrElse DdsExts.Contains(FileExt)) Then Continue For
            Dim RelPath As String = FilePath.Substring(CliOpts.InputPath.Length).TrimStart(Path.DirectorySeparatorChar)
            Dim RelDir As String = Path.GetDirectoryName(RelPath)
            Dim BaseName As String = Path.GetFileNameWithoutExtension(FilePath)
            Dim TargetRefDir As String = Path.Combine(CliOpts.ReferencePath, RelDir)
            Dim RefFile As String = ""
            If Directory.Exists(TargetRefDir) Then
                Dim PossibleMatches = Directory.GetFiles(TargetRefDir, BaseName & ".*")
                For Each Match In PossibleMatches
                    Dim MatchExt = Path.GetExtension(Match)
                    If ImgExts.Contains(MatchExt) OrElse DdsExts.Contains(MatchExt) Then
                        RefFile = Match
                        Exit For
                    End If
                Next
            End If
            If RefFile <> "" AndAlso File.Exists(RefFile) Then
                RunQualityMetrics(FilePath, RefFile, CliOpts)
            Else
                Console.WriteLine($"[SKIPPED] {RelPath}: Matching reference file not found in target directory.")
            End If
        Next
    End Sub

    Private Sub RunQualityMetrics(Source As String, Reference As String, CliOpts As CliOptions)
        Try
            Dim BmpSource As BitmapSource = LoadBitmapForMetrics(Source)
            Dim BmpReference As BitmapSource = LoadBitmapForMetrics(Reference)
            Using Metrics As New ImageMetrics(BmpSource, BmpReference)
                Metrics.CalcAll()
                Console.WriteLine($"{Path.GetFileName(Source)} <-> {Path.GetFileName(Reference)} | MSE: {Metrics.MSE.Average:F4} | PSNR: {Metrics.PSNR.Average:F4} dB | SSIM: {Metrics.SSIM.Average:F4}")
                If CliOpts.Verbose Then
                    Console.WriteLine($"    Red   | MSE:{Metrics.MSE.R:F4} | PSNR:{Metrics.PSNR.R:F4} dB | SSIM:{Metrics.SSIM.R:F4}")
                    Console.WriteLine($"    Green | MSE:{Metrics.MSE.G:F4} | PSNR:{Metrics.PSNR.G:F4} dB | SSIM:{Metrics.SSIM.G:F4}")
                    Console.WriteLine($"    Blue  | MSE:{Metrics.MSE.B:F4} | PSNR:{Metrics.PSNR.B:F4} dB | SSIM:{Metrics.SSIM.B:F4}")
                    Console.WriteLine($"    Alpha | MSE:{Metrics.MSE.A:F4} | PSNR:{Metrics.PSNR.A:F4} dB | SSIM:{Metrics.SSIM.A:F4}")
                    Console.WriteLine()
                End If
            End Using
        Catch ex As Exception
            Console.WriteLine($"[FAILED]: {ex.Message}")
        End Try
    End Sub

    Private Function LoadBitmapForMetrics(FilePath As String) As BitmapSource
        If Path.GetExtension(FilePath).ToLower() = ".dds" Then
            Using DDSDecoder As New DDS_Decoder(FilePath)
                Return DDSDecoder.ToBitmapSource()
            End Using
        Else
            Return LoadBitmapSource(FilePath)
        End If
    End Function

    Private Sub ShowFileInfo(Source As String)
        Try
            Dim SourceExt = Path.GetExtension(Source).ToLower()
            Console.WriteLine($"--- {Path.GetFileName(Source)} ---")
            If SourceExt = ".dds" Then
                Using Decoder As New DDS_Decoder(Source)
                    Console.WriteLine(GetDDSReport(Decoder))
                End Using
            Else
                Dim TempImage As BitmapSource = LoadBitmapForMetrics(Source)
                Console.WriteLine(GetImageReport(TempImage, Source))
            End If
        Catch ex As Exception
            Console.WriteLine($"[FAILED] {Path.GetFileName(Source)}: {ex.Message}")
        End Try
    End Sub

    Private Function GetDDSReport(Source As DDS_Decoder) As String
        Dim ReportBuilder As New StringBuilder()
        ReportBuilder.AppendLine("===== Info =====")
        ReportBuilder.AppendLine()
        ReportBuilder.AppendLine("[Core Properties]")
        ReportBuilder.AppendLine($"Signature: {Source.Signature}")
        ReportBuilder.AppendLine($"Resolution: {Source.Width} x {Source.Height}")
        If Source.Depth > 0 Then ReportBuilder.AppendLine($"Depth: {Source.Depth}")
        ReportBuilder.AppendLine($"MipMap Count: {Source.MipMapCount}")
        ReportBuilder.AppendLine($"Pitch/Linear Size: {Source.PitchLinearSize} bytes")
        ReportBuilder.AppendLine($"Extended Header: {Source.ExtendedHeader}")
        ReportBuilder.AppendLine()
        ReportBuilder.AppendLine("[Pixel Format]")
        ReportBuilder.AppendLine($"Header Size: {Source.HeaderSize} bytes")
        ReportBuilder.AppendLine($"Sub-Header Size: {Source.SubHeaderSize} bytes")
        If Not Source.FourCC.Contains(vbNullChar) Then ReportBuilder.AppendLine($"FourCC: {Source.FourCC}")
        ReportBuilder.AppendLine($"RGB Bit Count: {Source.RGBBitCount}")
        ReportBuilder.AppendLine($"Red Bit Mask: 0x{Source.RedBitMask:X8}")
        ReportBuilder.AppendLine($"Green Bit Mask: 0x{Source.GreenBitMask:X8}")
        ReportBuilder.AppendLine($"Blue Bit Mask: 0x{Source.BlueBitMask:X8}")
        ReportBuilder.AppendLine($"Alpha Bit Mask: 0x{Source.AlphaBitMask:X8}")
        ReportBuilder.AppendLine()
        ReportBuilder.AppendLine("[Surface & Capabilities]")
        ReportBuilder.AppendLine($"Surface Flags: {Source.SurfaceFlags}")
        ReportBuilder.AppendLine($"Pixel Flags: {Source.PixelFlags}")
        ReportBuilder.AppendLine($"Caps 1: {Source.Caps1}")
        ReportBuilder.AppendLine($"Caps 2: {Source.Caps2}")
        If Source.ExtendedHeader Then
            ReportBuilder.AppendLine()
            ReportBuilder.AppendLine("[DX10 Extended Header]")
            ReportBuilder.AppendLine($"DXGI Format: {Source.DXGIFormat}")
            ReportBuilder.AppendLine($"Dimension: {Source.ResourceDimension}")
            ReportBuilder.AppendLine($"Array Size: {Source.ArraySize}")
            ReportBuilder.AppendLine($"Misc Flag 1: {Source.MiscFlag}")
            ReportBuilder.AppendLine($"Misc Flag 2: {Source.MiscFlags2}")
        End If
        Return ReportBuilder.ToString()
    End Function

    Private Function GetImageReport(Source As BitmapSource, SourcePath As String) As String
        Dim ReportBuilder As New StringBuilder()
        Dim BitsPerPixel As Integer = Source.Format.BitsPerPixel
        Dim UncompressedBytes As Long = CLng(Source.Width) * Source.Height * (BitsPerPixel \ 8)
        Dim extension As String = Path.GetExtension(SourcePath).ToUpper().Replace(".", "")
        ReportBuilder.AppendLine("===== Info =====")
        ReportBuilder.AppendLine()
        ReportBuilder.AppendLine("[Core Properties]")
        ReportBuilder.AppendLine($"Resolution: {Source.Width} x {Source.Height}")
        ReportBuilder.AppendLine($"Source Format: {extension}")
        ReportBuilder.AppendLine($"DPI (Print Res): {Math.Round(Source.DpiX)} x {Math.Round(Source.DpiY)} DPI")
        ReportBuilder.AppendLine()
        ReportBuilder.AppendLine("[Pixel Format]")
        ReportBuilder.AppendLine($"Format Layout: {Source.Format.ToString}")
        ReportBuilder.AppendLine($"Has Alpha Channel: {If(WicAlphaFormats.Contains(Source.Format), "Yes", "No")}")
        ReportBuilder.AppendLine($"Bit Depth: {BitsPerPixel} bits per pixel")
        ReportBuilder.AppendLine($"Uncompressed Size: {UncompressedBytes:N0} bytes")
        Return ReportBuilder.ToString()
    End Function

    Private Function BuildOutputPath(SourceFile As String, SourceDir As String, TargetDir As String, TargetExt As String) As String
        Dim ResultPath As String
        Dim FullSourceDir As String = Path.GetFullPath(SourceDir)
        Dim FullSourcePath As String = Path.GetFullPath(SourceFile)
        If String.IsNullOrEmpty(TargetDir) Then Return Path.ChangeExtension(SourceFile, TargetExt)
        If Not FullSourceDir.EndsWith(Path.DirectorySeparatorChar) Then FullSourceDir &= Path.DirectorySeparatorChar
        If FullSourcePath.StartsWith(FullSourceDir, StringComparison.OrdinalIgnoreCase) Then
            Dim PartialPath As String = FullSourcePath.Substring(FullSourceDir.Length)
            ResultPath = Path.Combine(TargetDir, PartialPath)
        Else
            ResultPath = Path.Combine(TargetDir, Path.GetFileName(SourceFile))
        End If
        ResultPath = Path.ChangeExtension(ResultPath, TargetExt)
        Directory.CreateDirectory(Path.GetDirectoryName(ResultPath))
        Return ResultPath
    End Function

    Private Function GetFormatFromString(Source As String) As DXGI_Format
        Dim TempFormat As DXGI_Format
        If FormatAliases.TryGetValue(Source, TempFormat) Then
            Return TempFormat
        Else
            Throw New Exception($"Unsupported format: {Source}")
        End If
    End Function

    Private Sub DisplayHelp()
        Console.WriteLine("TexInspectCLI v1.3.5")
        Console.WriteLine("Usage: TexInspectCLI.exe <input_path> [options]")
        Console.WriteLine()
        Console.WriteLine("Options:")
        Console.WriteLine("  -fmt <format>                  Target Format (e.g., BC7_UNORM, DXT1, ATI2). Default: BC7_UNORM_SRGB")
        Console.WriteLine("  -m                             Generate Mipmaps")
        Console.WriteLine("  -ndx, --nodx10                 Force legacy DDS header (Implicitly enabled for DXT/ATI formats)")
        Console.WriteLine("  -o <path>                      Output file or directory")
        Console.WriteLine("  -ext <extension>               Output extension for batch decoding (e.g., .jpg, .bmp). Default: .png")
        Console.WriteLine("  -r, --recursive                Search subdirectories when processing a folder")
        Console.WriteLine("  -f, --force                    Suppress warnings and overwrite files")
        Console.WriteLine("  --info                         Show header info for the target file(s) without processing")
        Console.WriteLine("  -q, --quality <path>           Compare input to reference path and print average MSE, PSNR, & SSIM")
        Console.WriteLine("  -qv, --qualityverbose <path>   Compare input to reference path and print average & per-channel metrics")
        Console.WriteLine("  -t,                            Output elapsed time for the operation")
        Console.WriteLine()
        Console.WriteLine("Examples:")
        Console.WriteLine("  Encode PNG to BC7 DDS:")
        Console.WriteLine("    TexInspectCLI.exe texture.png -fmt BC7_UNORM_SRGB -m" & vbCrLf)
        Console.WriteLine("  Encode PNG to legacy DXT5 DDS:")
        Console.WriteLine("    TexInspectCLI.exe texture.png -fmt DXT5" & vbCrLf)
        Console.WriteLine("  Encode a folder of images to DDS into a specific output folder:")
        Console.WriteLine("    TexInspectCLI.exe ""C:\InputImages"" -o ""C:\OutputDDS"" -fmt BC7_UNORM_SRGB -m -f" & vbCrLf)
        Console.WriteLine("  Decode a folder of DDS files to JPEGs:")
        Console.WriteLine("    TexInspectCLI.exe ""C:\InputDDS"" -o ""C:\OutputJPEGs"" -ext .jpg -f" & vbCrLf)
        Console.WriteLine("  View header data of a specific file:")
        Console.WriteLine("    TexInspectCLI.exe texture.dds --info" & vbCrLf)
        Console.WriteLine("  Generate quality metrics between two files:")
        Console.WriteLine("    TexInspectCLI.exe texture.dds -q texture.png")
    End Sub

End Module