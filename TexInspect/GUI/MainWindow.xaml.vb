Imports System.IO
Imports System.Text
Imports Microsoft.Win32
Imports System.Windows.Media.Media3D

Partial Public Class MainWindow
    Inherits Window

    Private Debug As Integer = 0

    Private FilePath As String
    Private TempPath As String
    Private FilePaths() As String
    Private PreviewImage As BitmapSource

    Private CubeMode As Boolean = False
    Private IsDragging As Boolean = False
    Private IsZooming As Boolean = False
    Private LastMousePos As Point

    Private CubeTransform As Transform3DGroup
    Private CubeRotation As QuaternionRotation3D
    Private CubeScaleTransform As ScaleTransform3D
    Private CurrentOrientation As Quaternion = Quaternion.Identity

    Private SpecialFlags As DDS_SpecialFlags
    Private Options As ParallelOptions
    Private CubeFaces(5) As CubeFace

    Private Class CubeFace
        Public Image As BitmapSource
        Public PreviewImage As BitmapSource
    End Class

    Private Sub MainWindow_Loaded(sender As Object, e As RoutedEventArgs) Handles MyBase.Loaded
        Me.AllowDrop = True
        OutputFormatComboBox.SelectedIndex = 0
        CubeTransform = New Transform3DGroup()
        CubeRotation = New QuaternionRotation3D(CurrentOrientation)
        CubeScaleTransform = New ScaleTransform3D(1.0, 1.0, 1.0)
        CubeTransform.Children.Add(New RotateTransform3D(CubeRotation))
        CubeTransform.Children.Add(CubeScaleTransform)
        Dim Args As String() = Environment.GetCommandLineArgs()
        If Args.Count > 1 Then
            For i = 1 To Args.Count - 1
                If Args(i) = "-h" Then
                    Me.Title &= " - All Cores Mode"
                Else
                    Options = New ParallelOptions With {.MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)}
                End If
            Next
        End If
    End Sub

    Private Sub MainWindow_DragEnter(sender As Object, e As DragEventArgs) Handles Me.DragEnter
        If e.Data.GetDataPresent(DataFormats.FileDrop) Then
            e.Effects = DragDropEffects.Copy
        Else
            e.Effects = DragDropEffects.None
        End If
    End Sub

    Private Async Sub MainWindow_DragDrop(sender As Object, e As DragEventArgs) Handles Me.Drop
        Try
            Dim DroppedFiles() As String = DirectCast(e.Data.GetData(DataFormats.FileDrop), String())
            If DroppedFiles.Length > 0 Then
                Dim FileExt As String = Path.GetExtension(DroppedFiles(0)).ToLower
                If {".dds", ".png", ".jpg", ".jpeg", ".bmp"}.Contains(FileExt) Then
                    Await ProcessLoadedFileAsync(DroppedFiles(0))
                Else
                    MessageBox.Show($"Invalid format: {FileExt}", "Error", MessageBoxButton.OK, MessageBoxImage.Information)
                End If
            End If
        Catch ex As Exception
            MessageBox.Show("Error loading file: " & ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Exclamation)
        End Try
    End Sub

    Private Sub MainWindow_Closing(sender As Object, e As System.ComponentModel.CancelEventArgs) Handles Me.Closing
        If TempPath IsNot Nothing AndAlso Directory.Exists(TempPath) Then
            Directory.Delete(TempPath, True)
        End If
    End Sub

    Public Sub UpdateOverrideFormats(sender As Object, e As RoutedEventArgs) Handles CompressionCheckBox.Checked, CompressionCheckBox.Unchecked, SmoothAlphaRB.Checked, SharpAlphaRB.Checked, NoAlphaRB.Checked, ExtendedHeaderCheckBox.Checked, ExtendedHeaderCheckBox.Unchecked, NormalCheckBox.Checked, NormalCheckBox.Unchecked, PreMultAlphaRB.Checked, PreMultAlphaRB.Unchecked
        If OverrideComboBox Is Nothing Then Return
        Dim IsDX10 As Boolean = ExtendedHeaderCheckBox.IsChecked = True
        Dim AlphaMode As Integer = GetAlphaMode()
        OverrideComboBox.Items.Clear()
        PopulateOverrideFormats(IsDX10, AlphaMode)
        SelectFirstItem(OverrideComboBox)
        OverrideComboBox.IsEnabled = (OverrideComboBox.Items.Count > 1)
    End Sub

    Private Async Sub LoadImageButton_Click(sender As Object, e As RoutedEventArgs) Handles LoadImageButton.Click
        Dim OFD As New OpenFileDialog With {.Filter = "Image Files|*.png;*.jpg;*.bmp;*.dds"}
        If OFD.ShowDialog() = True Then
            Await ProcessLoadedFileAsync(OFD.FileName)
        End If
    End Sub

    Private Async Function ProcessLoadedFileAsync(TargetFilePath As String) As Task
        ResetUIAndState()
        FilePath = TargetFilePath
        Dim Extension As String = Path.GetExtension(FilePath).ToLower()
        If Extension = ".dds" Then
            Await LoadDDSFileAsync(FilePath)
        Else
            LoadStandardImage(FilePath)
        End If
        UpdatePreviewState()
        UpdateOverrideFormats(Nothing, Nothing)
    End Function

    Private Sub ResetUIAndState()
        DisposeCubeFaces()
        InfoTextBox.Clear()
        If PreviewImage IsNot Nothing Then PreviewImage = Nothing
        PreviewImage = Nothing
        FilePaths = Nothing
        CubeMode = False
        Preview2DViewer.Source = Nothing
        DragDropTextPanel.Visibility = Visibility.Visible
        If Cube3DGroup IsNot Nothing Then Cube3DGroup.Children.Clear()
        If TempPath IsNot Nothing AndAlso Directory.Exists(TempPath) Then Directory.Delete(TempPath, True)
        GC.Collect()
    End Sub

    Private Async Function LoadDDSFileAsync(Path As String) As Task
        Try
            Using DDSDecoder As New DDS_Decoder(Path)
                InfoTextBox.Text = GetDDSReport(DDSDecoder)
                If DDSDecoder.IsCubeMap Then
                    CubeMode = True
                    Dim TempCubeMaps As BitmapSource() = Await Task.Run(Function() DDSDecoder.ToCubeBitmapSources())
                    LoadCubeMaps(CubeFaces, TempCubeMaps)
                    Build3DCubeMap()
                Else
                    PreviewImage = Await Task.Run(Function() DDSDecoder.ToBitmapSource)
                End If
            End Using
            ToggleExportButtons(isDDS:=True)
        Catch ex As Exception
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Exclamation)
            ToggleExportButtons(isDDS:=False, isError:=True)
            PreviewImage = Nothing
        End Try
    End Function

    Private Sub LoadStandardImage(Path As String)
        Dim DetectedFaces As String() = DetectCubeFiles(Path)
        If DetectedFaces Is Nothing Then DetectedFaces = DetectCompositeCube(Path)
        If DetectedFaces IsNot Nothing Then
            CubeMode = True
            FilePaths = DetectedFaces
            Dim TempCubeMaps(5) As BitmapSource
            For i As Integer = 0 To 5
                TempCubeMaps(i) = LoadBitmapSource(DetectedFaces(i))
            Next
            LoadCubeMaps(CubeFaces, TempCubeMaps)
            Build3DCubeMap()
            InfoTextBox.Text = GetImageReport(TempCubeMaps(0), DetectedFaces(0))
        Else
            Dim TempImage As BitmapSource = LoadBitmapSource(Path)
            InfoTextBox.Text = GetImageReport(TempImage, Path)
            PreviewImage = TempImage
        End If
        ToggleExportButtons(isDDS:=False)
    End Sub

    Private Sub ToggleExportButtons(isDDS As Boolean, Optional isError As Boolean = False)
        If isError Then
            DDSExportGroup.IsEnabled = False
            ImageExportGroup.IsEnabled = False
            BenchGroupBox.IsEnabled = False
            Return
        End If
        DDSExportGroup.IsEnabled = Not isDDS
        ImageExportGroup.IsEnabled = isDDS
        EncBenchButton.IsEnabled = Not isDDS
        DecBenchButton.IsEnabled = isDDS
        BenchGroupBox.IsEnabled = True
    End Sub

    Private Sub UpdatePreviewState()
        DragDropTextPanel.Visibility = Visibility.Collapsed
        If CubeMode Then
            Preview2DViewer.Visibility = Visibility.Collapsed
            Preview3DViewer.Visibility = Visibility.Visible
        Else
            Preview3DViewer.Visibility = Visibility.Collapsed
            Preview2DViewer.Visibility = Visibility.Visible
            Preview2DViewer.Source = PreviewImage
        End If
    End Sub

    Private Sub ExportImageButton_Click(sender As Object, e As RoutedEventArgs) Handles ExportImageButton.Click
        If OutputFormatComboBox.SelectedItem Is Nothing Then Return
        Dim FileExt As String = OutputFormatComboBox.Text.ToString
        Dim Filter As String = $"{FileExt} Files|*.{FileExt.ToLower()}|All Files|*.*"
        Dim SFD As New SaveFileDialog With {.Filter = Filter, .FileName = Path.GetFileNameWithoutExtension(FilePath)}
        If SFD.ShowDialog() = True Then
            ToggleBusyState(True)
            If CubeMode Then
                For i = 0 To 5
                    Dim encoder As BitmapEncoder = GetEncoderFromExtension(FileExt)
                    encoder.Frames.Add(BitmapFrame.Create(CubeFaces(i).Image))
                    Dim outPath = SFD.FileName.Substring(0, SFD.FileName.Length - 4) & CubeSuffixes(i) & "." & FileExt.ToLower()
                    Using fs As New FileStream(outPath, FileMode.Create, FileAccess.Write)
                        encoder.Save(fs)
                    End Using
                Next
            Else
                Dim encoder As BitmapEncoder = GetEncoderFromExtension(FileExt)
                encoder.Frames.Add(BitmapFrame.Create(PreviewImage))
                Using fs As New FileStream(SFD.FileName, FileMode.Create, FileAccess.Write)
                    encoder.Save(fs)
                End Using
            End If
            ToggleBusyState(False)
        End If
    End Sub

    Private Async Sub ExportDDSButton_Click(sender As Object, e As RoutedEventArgs) Handles ExportDDSButton.Click
        If OverrideComboBox.SelectedItem Is Nothing Then Return
        Dim targetFormat As DXGI_Format = GetFormatFromString(OverrideComboBox.SelectedItem.ToString())
        Dim isLegacy As Boolean = Not (ExtendedHeaderCheckBox.IsChecked = True)
        Dim doMipMaps As Boolean = MipMapCheckBox.IsChecked = True
        Dim ddsSpecialFlags As DDS_SpecialFlags = SpecialFlags
        Dim SFD As New SaveFileDialog With {.Filter = "DDS Files|*.dds|All Files|*.*", .FileName = Path.GetFileNameWithoutExtension(FilePath)}
        If SFD.ShowDialog() = True Then
            ToggleBusyState(True)
            If ddsSpecialFlags = DDS_SpecialFlags.DDS_DXT5n Then SFD.FileName = SFD.FileName.Replace(".dds", "_n.dds")
            If CubeMode AndAlso FilePaths IsNot Nothing Then
                Using DDSEncoder As New DDS_Encoder(FilePaths, targetFormat, doMipMaps, isLegacy)
                    Await Task.Run(Sub() DDSEncoder.Save(SFD.FileName))
                End Using
            Else
                Using DDSEncoder As New DDS_Encoder(FilePath, targetFormat, doMipMaps, isLegacy, ddsSpecialFlags)
                    Await Task.Run(Sub() DDSEncoder.Save(SFD.FileName))
                End Using
            End If
            ToggleBusyState(False)
            If TempPath IsNot Nothing AndAlso Directory.Exists(TempPath) Then
                Directory.Delete(TempPath, True)
            End If
        End If
    End Sub

    Private Async Sub EncBenchButton_Click(sender As Object, e As RoutedEventArgs) Handles EncBenchButton.Click
        If OverrideComboBox.SelectedItem Is Nothing Then Return
        Dim TempFmt As String = OverrideComboBox.SelectedItem.ToString()
        Dim TempMips As Boolean = MipMapCheckBox.IsChecked = True
        Dim TempDX10 As Boolean = ExtendedHeaderCheckBox.IsChecked = True
        ToggleBusyState(True)
        Await RunBenchmarkAsync(Sub(FileName)
                                    Dim targetFormat As DXGI_Format = GetFormatFromString(TempFmt)
                                    Using Encoder As New DDS_Encoder(FileName, targetFormat, TempMips, Not TempDX10, SpecialFlags)
                                        Encoder.Save(Path.Combine(TempPath, "encoded.dds"))
                                    End Using
                                End Sub)
        ToggleBusyState(False)
    End Sub

    Private Async Sub DecBenchButton_Click(sender As Object, e As RoutedEventArgs) Handles DecBenchButton.Click
        ToggleBusyState(True)
        Dim FileExt As String = $".{OutputFormatComboBox.Text.ToString.ToLower}"
        Await RunBenchmarkAsync(Sub(FileName)
                                    Using Decoder As New DDS_Decoder(FileName)
                                        Decoder.Save(Path.Combine(TempPath, $"decoded{FileExt}"), FileExt)
                                    End Using
                                End Sub)
        ToggleBusyState(False)
    End Sub

    Private Async Function RunBenchmarkAsync(BenchAction As Action(Of String)) As Task
        TempPath = Path.Combine(Path.GetTempPath(), "TexTemp\")
        Directory.CreateDirectory(TempPath)
        Dim BenchTimer As Stopwatch = Stopwatch.StartNew()
        Await Task.Run(Sub()
                           For i = 0 To 49
                               BenchAction(FilePath)
                           Next
                       End Sub)
        BenchTimer.Stop()
        MessageBox.Show($"Average: {BenchTimer.ElapsedMilliseconds / 50} ms", "Benchmark Result", MessageBoxButton.OK, MessageBoxImage.Information)
    End Function

    Private Sub CalcMetricsButton_Click(sender As Object, e As RoutedEventArgs) Handles CalcMetricsButton.Click
        If PreviewImage Is Nothing Then Return
        Dim OFD As New OpenFileDialog With {.Filter = "Image Files|*.png;*.jpg;*.bmp;*.dds"}
        If OFD.ShowDialog() = True Then
            Dim QualityReport As String = ""
            Dim TempImage As BitmapSource = LoadBitmapForMetrics(OFD.FileName)
            Using QualityTest As New ImageMetrics(PreviewImage, TempImage)
                QualityTest.CalcAll()
                QualityReport = $"MSE: {Math.Round(QualityTest.MSE.Average, 4)} {vbCrLf} PSNR: {Math.Round(QualityTest.PSNR.Average, 4)} {vbCrLf} SSIM: {Math.Round(QualityTest.SSIM.Average, 4)}"
            End Using
            MessageBox.Show(QualityReport, "Report", MessageBoxButton.OK, MessageBoxImage.Information)
        End If
    End Sub

    Private Function LoadBitmapForMetrics(TargetFilePath As String) As BitmapSource
        If Path.GetExtension(TargetFilePath).ToLower() = ".dds" Then
            Using DDSDecoder As New DDS_Decoder(TargetFilePath)
                Return DDSDecoder.ToBitmapSource()
            End Using
        End If
        Return LoadBitmapSource(TargetFilePath)
    End Function

    Private Sub Build3DCubeMap()
        If Cube3DGroup Is Nothing Then Return
        Cube3DGroup.Children.Clear()
        Dim p0 As New Point3D(-1, 1, 1)
        Dim p1 As New Point3D(1, 1, 1)
        Dim p2 As New Point3D(1, -1, 1)
        Dim p3 As New Point3D(-1, -1, 1)
        Dim p4 As New Point3D(-1, 1, -1)
        Dim p5 As New Point3D(1, 1, -1)
        Dim p6 As New Point3D(1, -1, -1)
        Dim p7 As New Point3D(-1, -1, -1)
        Cube3DGroup.Children.Add(CreateFaceMesh(p1, p5, p6, p2, CubeFaces(0).PreviewImage)) ' Right (+X)
        Cube3DGroup.Children.Add(CreateFaceMesh(p4, p0, p3, p7, CubeFaces(1).PreviewImage)) ' Left (-X)
        Cube3DGroup.Children.Add(CreateFaceMesh(p4, p5, p1, p0, CubeFaces(2).PreviewImage)) ' Top (+Y)
        Cube3DGroup.Children.Add(CreateFaceMesh(p3, p2, p6, p7, CubeFaces(3).PreviewImage)) ' Bottom (-Y)
        Cube3DGroup.Children.Add(CreateFaceMesh(p0, p1, p2, p3, CubeFaces(4).PreviewImage)) ' Front (+Z)
        Cube3DGroup.Children.Add(CreateFaceMesh(p5, p4, p7, p6, CubeFaces(5).PreviewImage)) ' Back (-Z)
        Cube3DGroup.Transform = CubeTransform
    End Sub

    Private Function CreateFaceMesh(topLeft As Point3D, topRight As Point3D, bottomRight As Point3D, bottomLeft As Point3D, bmp As BitmapSource) As GeometryModel3D
        Dim mesh As New MeshGeometry3D()
        mesh.Positions.Add(topLeft)
        mesh.Positions.Add(topRight)
        mesh.Positions.Add(bottomRight)
        mesh.Positions.Add(bottomLeft)
        mesh.TextureCoordinates.Add(New Point(0, 0))
        mesh.TextureCoordinates.Add(New Point(1, 0))
        mesh.TextureCoordinates.Add(New Point(1, 1))
        mesh.TextureCoordinates.Add(New Point(0, 1))
        mesh.TriangleIndices.Add(0) : mesh.TriangleIndices.Add(2) : mesh.TriangleIndices.Add(1)
        mesh.TriangleIndices.Add(0) : mesh.TriangleIndices.Add(3) : mesh.TriangleIndices.Add(2)
        Dim brush As New ImageBrush(bmp)
        Dim material As New DiffuseMaterial(brush)
        Return New GeometryModel3D(mesh, material)
    End Function

    Private Sub PreviewViewer_MouseDown(sender As Object, e As MouseButtonEventArgs) Handles Preview3DViewer.MouseDown, Preview2DViewer.MouseDown
        If e.ChangedButton = MouseButton.Left Then
            IsDragging = True
            LastMousePos = e.GetPosition(Me)
            Mouse.Capture(DirectCast(sender, IInputElement))
        ElseIf e.ChangedButton = MouseButton.Middle Then
            CurrentOrientation = Quaternion.Identity
            CubeRotation.Quaternion = CurrentOrientation
            CubeScaleTransform.ScaleX = 1.0 : CubeScaleTransform.ScaleY = 1.0 : CubeScaleTransform.ScaleZ = 1.0
        ElseIf e.ChangedButton = MouseButton.Right Then
            IsZooming = True
            LastMousePos = e.GetPosition(Me)
            Mouse.Capture(DirectCast(sender, IInputElement))
        End If
    End Sub

    Private Sub PreviewViewer_MouseUp(sender As Object, e As MouseButtonEventArgs) Handles Preview3DViewer.MouseUp, Preview2DViewer.MouseUp
        IsDragging = False
        IsZooming = False
        Mouse.Capture(Nothing)
    End Sub

    Private Sub PreviewViewer_MouseMove(sender As Object, e As MouseEventArgs) Handles Preview3DViewer.MouseMove, Preview2DViewer.MouseMove
        Dim currentPos = e.GetPosition(Me)
        If IsDragging Then
            Dim deltaX = (currentPos.X - LastMousePos.X) * 0.5
            Dim deltaY = (currentPos.Y - LastMousePos.Y) * 0.5
            Dim qX As New Quaternion(New Vector3D(1, 0, 0), deltaY)
            Dim qY As New Quaternion(New Vector3D(0, 1, 0), deltaX)
            CurrentOrientation = qY * qX * CurrentOrientation
            CubeRotation.Quaternion = CurrentOrientation
            LastMousePos = currentPos
        ElseIf IsZooming Then
            Dim delta = ((currentPos.X - currentPos.Y) - (LastMousePos.X - LastMousePos.Y)) * 0.01
            CubeScaleTransform.ScaleX = Math.Max(0.1, CubeScaleTransform.ScaleX + delta)
            CubeScaleTransform.ScaleY = Math.Max(0.1, CubeScaleTransform.ScaleY + delta)
            CubeScaleTransform.ScaleZ = Math.Max(0.1, CubeScaleTransform.ScaleZ + delta)
            LastMousePos = currentPos
        End If
    End Sub

    Private Sub LoadCubeMaps(CubeFaces As CubeFace(), CubeMapImages As BitmapSource())
        For i As Integer = 0 To 5
            CubeFaces(i) = New CubeFace With {
            .Image = CubeMapImages(i),
            .PreviewImage = CType(GetPreviewImage(CubeMapImages(i)), BitmapSource)
        }
        Next
    End Sub

    Private Function GetEncoderFromExtension(Ext As String) As BitmapEncoder
        Select Case Ext.ToUpper()
            Case "PNG" : Return New PngBitmapEncoder()
            Case "JPG", "JPEG" : Return New JpegBitmapEncoder()
            Case "BMP" : Return New BmpBitmapEncoder()
            Case Else : Return New PngBitmapEncoder()
        End Select
    End Function

    Private Function GetAlphaMode() As Integer
        If NoAlphaRB.IsChecked = True Then Return 0
        If SharpAlphaRB.IsChecked = True Then Return 1
        If SmoothAlphaRB.IsChecked = True Then Return 2
        If PreMultAlphaRB.IsChecked = True Then Return 3
        Return 0
    End Function

    Private Sub PopulateOverrideFormats(IsDX10 As Boolean, AlphaMode As Integer)
        If NormalCheckBox.IsChecked = True Then
            If CompressionCheckBox.IsChecked = True Then
                OverrideComboBox.Items.Add(If(IsDX10, "BC5 UNORM", "ATI2 (BC5)"))
                If IsDX10 Then OverrideComboBox.Items.Add("BC7n UNORM")
                OverrideComboBox.Items.Add(If(IsDX10, "BC3n sRGB", "DXT5n"))
            Else
                OverrideComboBox.Items.Add("BGRX (B8G8R8X8)")
            End If
        ElseIf CompressionCheckBox.IsChecked = True Then
            Select Case AlphaMode
                Case 0
                    If IsDX10 Then OverrideComboBox.Items.Add("BC7 sRGB")
                    OverrideComboBox.Items.Add(If(IsDX10, "BC1 sRGB", "DXT1"))
                    OverrideComboBox.Items.Add(If(IsDX10, "BC4 UNORM", "ATI1 (BC4)"))
                Case 1
                    OverrideComboBox.Items.Add(If(IsDX10, "BC1a sRGB", "DXT1a"))
                    OverrideComboBox.Items.Add(If(IsDX10, "BC2 sRGB", "DXT3"))
                Case 2
                    If IsDX10 Then OverrideComboBox.Items.Add("BC7 sRGB")
                    OverrideComboBox.Items.Add(If(IsDX10, "BC3 sRGB", "DXT5"))
                Case 3
                    OverrideComboBox.Items.Add(If(IsDX10, "BC2p sRGB", "DXT2"))
                    OverrideComboBox.Items.Add(If(IsDX10, "BC3p sRGB", "DXT4"))
            End Select
        Else
            Select Case AlphaMode
                Case 0 : OverrideComboBox.Items.Add("BGRX (B8G8R8X8)")
                Case 1, 2 : OverrideComboBox.Items.Add("BGRA (B8G8R8A8)")
            End Select
        End If
    End Sub

    Private Function GetFormatFromString(FormatName As String) As DXGI_Format
        Select Case FormatName
            Case "BC1 sRGB", "DXT1" : SpecialFlags = DDS_SpecialFlags.DDS_DXT1o : Return DXGI_Format.DXGI_FORMAT_BC1_UNORM_SRGB
            Case "BC1a sRGB", "DXT1a" : Return DXGI_Format.DXGI_FORMAT_BC1_UNORM_SRGB
            Case "BC2p sRGB", "DXT2" : SpecialFlags = DDS_SpecialFlags.DDS_DXT2 : Return DXGI_Format.DXGI_FORMAT_BC2_UNORM_SRGB
            Case "BC2 sRGB", "DXT3" : Return DXGI_Format.DXGI_FORMAT_BC2_UNORM_SRGB
            Case "BC3p sRGB", "DXT4" : SpecialFlags = DDS_SpecialFlags.DDS_DXT4 : Return DXGI_Format.DXGI_FORMAT_BC3_UNORM_SRGB
            Case "BC3 sRGB", "DXT5" : Return DXGI_Format.DXGI_FORMAT_BC3_UNORM_SRGB
            Case "BC3n sRGB", "DXT5n" : SpecialFlags = DDS_SpecialFlags.DDS_DXT5n : Return DXGI_Format.DXGI_FORMAT_BC3_UNORM_SRGB
            Case "BC4 UNORM", "ATI1 (BC4)" : Return DXGI_Format.DXGI_FORMAT_BC4_UNORM
            Case "BC5 UNORM", "ATI2 (BC5)" : Return DXGI_Format.DXGI_FORMAT_BC5_UNORM
            Case "BC7 sRGB" : Return DXGI_Format.DXGI_FORMAT_BC7_UNORM_SRGB
            Case "BC7n UNORM" : SpecialFlags = DDS_SpecialFlags.DDS_BC7n : Return DXGI_Format.DXGI_FORMAT_BC7_UNORM
            Case "BGRX (B8G8R8X8)" : Return DXGI_Format.DXGI_FORMAT_B8G8R8X8_UNORM_SRGB
            Case "BGRA (B8G8R8A8)" : Return DXGI_Format.DXGI_FORMAT_B8G8R8A8_UNORM_SRGB
            Case Else : Throw New Exception($"Unsupported format: {FormatName}")
        End Select
    End Function

    Public Function DetectCubeFiles(SourceFilePath As String) As String()
        Dim DirectoryName As String = Path.GetDirectoryName(SourceFilePath)
        Dim FileNameWithoutExt As String = Path.GetFileNameWithoutExtension(SourceFilePath)
        Dim Extension As String = Path.GetExtension(SourceFilePath)
        If FileNameWithoutExt.Length < 3 Then Return Nothing
        Dim BaseName As String = ""
        Dim IsCubeFace As Boolean = False
        For Each suffix As String In CubeSuffixes
            If FileNameWithoutExt.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) Then
                BaseName = FileNameWithoutExt.Substring(0, FileNameWithoutExt.Length - 3)
                IsCubeFace = True
                Exit For
            End If
        Next
        If Not IsCubeFace Then Return Nothing
        Dim DetectedFaces(5) As String
        For i As Integer = 0 To 5
            Dim FacePath As String = Path.Combine(DirectoryName, BaseName & CubeSuffixes(i) & Extension)
            If File.Exists(FacePath) Then
                DetectedFaces(i) = FacePath
            Else
                Return Nothing
            End If
        Next
        If MessageBox.Show("CubeMap Detected. Load all faces?", "CubeMap Detected", MessageBoxButton.YesNo, MessageBoxImage.Question) = MessageBoxResult.Yes Then
            Return DetectedFaces
        Else
            Return Nothing
        End If
    End Function

    Private Function DetectCompositeCube(Source As String) As String()
        Dim TempWidth As Long, TempHeight As Long
        Using fs As New FileStream(Source, FileMode.Open, FileAccess.Read, FileShare.Read)
            Dim decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None)
            TempWidth = decoder.Frames(0).PixelWidth
            TempHeight = decoder.Frames(0).PixelHeight
        End Using
        If (TempWidth * 6 = TempHeight) OrElse (TempHeight * 6 = TempWidth) OrElse (TempWidth * 4 = TempHeight * 3) OrElse (TempWidth * 3 = TempHeight * 4) Then
        Else
            Return Nothing
        End If
        If MessageBox.Show("Potential CubeMap detected. Slice and load?", "CubeMap Detected", MessageBoxButton.YesNo, MessageBoxImage.Information) = MessageBoxResult.Yes Then
            Dim Slicer As New CubeSlicer(Source)
            Dim Result As String() = New String(5) {}
            TempPath = Path.Combine(Path.GetTempPath(), "TexTemp\")
            Directory.CreateDirectory(TempPath)
            Slicer.SaveBitmaps(TempPath)
            For i As Integer = 0 To 5
                Dim FacePath As String = Path.Combine(TempPath, $"Face{CubeSuffixes(i)}.png")
                If File.Exists(FacePath) Then
                    Result(i) = FacePath
                End If
            Next
            Return Result
        End If
        Return Nothing
    End Function

    Public Function GetPreviewImage(Source As BitmapSource) As BitmapSource
        If Source Is Nothing Then Throw New ArgumentNullException(NameOf(Source))
        If Source.PixelWidth <= 512 AndAlso Source.PixelHeight <= 512 Then Return Source
        Dim scaleX As Double = 512.0 / Source.PixelWidth
        Dim scaleY As Double = 512.0 / Source.PixelHeight
        Dim scale As Double = Math.Min(scaleX, scaleY)
        Dim tb As New TransformedBitmap(Source, New ScaleTransform(scale, scale))
        tb.Freeze()
        Return tb
    End Function

    Private Sub ToggleBusyState(IsBusy As Boolean)
        LoadImageButton.IsEnabled = Not IsBusy
        ExportImageButton.IsEnabled = Not IsBusy
        ExportDDSButton.IsEnabled = Not IsBusy
        EncBenchButton.IsEnabled = DDSExportGroup.IsEnabled AndAlso Not IsBusy
        DecBenchButton.IsEnabled = ImageExportGroup.IsEnabled AndAlso Not IsBusy
        CalcMetricsButton.IsEnabled = Not IsBusy
        GC.Collect()
    End Sub

    Private Sub DisposeCubeFaces()
        For Each Face In CubeFaces
            If Face IsNot Nothing Then
                If Face.Image IsNot Nothing Then
                    Face.Image = Nothing
                End If
                If Face.PreviewImage IsNot Nothing Then
                    Face.PreviewImage = Nothing
                End If
            End If
        Next
    End Sub

    Private Sub SelectFirstItem(SourceControl As ComboBox)
        If SourceControl IsNot Nothing AndAlso SourceControl.Items.Count > 0 Then
            SourceControl.SelectedIndex = 0
        End If
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
        Dim UncompressedBytes As Long = CLng(Source.PixelWidth) * Source.PixelHeight * (BitsPerPixel \ 8)
        Dim extension As String = Path.GetExtension(SourcePath).ToUpper().Replace(".", "")
        ReportBuilder.AppendLine("===== Info =====")
        ReportBuilder.AppendLine()
        ReportBuilder.AppendLine("[Core Properties]")
        ReportBuilder.AppendLine($"Resolution: {Source.PixelWidth} x {Source.PixelHeight}")
        ReportBuilder.AppendLine($"Source Format: {extension}")
        ReportBuilder.AppendLine($"DPI (Print Res): {Math.Round(Source.DpiX)} x {Math.Round(Source.DpiY)} DPI")
        ReportBuilder.AppendLine()
        ReportBuilder.AppendLine("[Pixel Format]")
        ReportBuilder.AppendLine($"Format Layout: {Source.Format.ToString()}")
        ReportBuilder.AppendLine($"Has Alpha Channel: {If(WicAlphaFormats.Contains(Source.Format), "Yes", "No")}")
        ReportBuilder.AppendLine($"Bit Depth: {BitsPerPixel} bits per pixel")
        ReportBuilder.AppendLine($"Uncompressed Size: {UncompressedBytes:N0} bytes")
        ReportBuilder.AppendLine()
        If FilePaths IsNot Nothing Then
            ReportBuilder.AppendLine($"[Extra Info]")
            ReportBuilder.AppendLine($"CubeMap Detected")
        End If
        Return ReportBuilder.ToString()
    End Function

End Class