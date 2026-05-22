Public Class ImageMetrics
    Implements IDisposable

    Public Disposed As Boolean

    Public Property MSE As ChannelMetric
    Public Property PSNR As ChannelMetric
    Public Property SSIM As ChannelMetric

    Private ImageBytes1 As Byte()
    Private ImageBytes2 As Byte()

    Private Width As Integer
    Private Height As Integer
    Private Stride As Integer

    Private MSE_Done As Boolean = False
    Private SSIM_Done As Boolean = False

    Private Const C1 As Double = 6.5025
    Private Const C2 As Double = 58.5225

    <ThreadStatic> Private Shared BufferA As Double()

    Public Structure ChannelMetric
        Public B As Double
        Public G As Double
        Public R As Double
        Public A As Double
        Public Average As Double
    End Structure

    Public Sub New(ImagePath1 As String, ImagePath2 As String)
        Dim Image1 As BitmapSource = LoadBitmapSource(ImagePath1)
        Dim Image2 As BitmapSource = LoadBitmapSource(ImagePath2)
        Initialize(Image1, Image2)
    End Sub

    Public Sub New(Image1 As BitmapSource, Image2 As BitmapSource)
        Initialize(Image1, Image2)
    End Sub

    Private Sub Initialize(Image1 As BitmapSource, Image2 As BitmapSource)
        Width = Image1.PixelWidth
        Height = Image1.PixelHeight
        If (Width <> Image2.PixelWidth) OrElse (Height <> Image2.PixelHeight) Then
            Throw New Exception("Images must have identical dimensions!")
        End If
        ImageBytes1 = GetNormalizedBytes(Image1)
        ImageBytes2 = GetNormalizedBytes(Image2)
        Stride = Width * 4
    End Sub

    Private Function GetNormalizedBytes(Source As BitmapSource) As Byte()
        Dim ConvertedSource As BitmapSource = Source
        If Source.Format <> PixelFormats.Bgra32 Then
            Dim Formatter As New FormatConvertedBitmap()
            Formatter.BeginInit()
            Formatter.Source = Source
            Formatter.DestinationFormat = PixelFormats.Bgra32
            Formatter.EndInit()
            Formatter.Freeze()
            ConvertedSource = Formatter
        End If
        Dim RowStride As Integer = ConvertedSource.PixelWidth * 4
        Dim ImageByteCount As Integer = RowStride * ConvertedSource.PixelHeight
        Dim NormalizedBytes(ImageByteCount - 1) As Byte
        ConvertedSource.CopyPixels(NormalizedBytes, RowStride, 0)
        Return NormalizedBytes
    End Function

    Public Sub CalcAll()
        CalcMSE_PSNR()
        CalcSSIM()
    End Sub

    Private Sub CalcMSE_PSNR()
        If Not MSE_Done Then
            Dim ErrorSums(3) As Double
            Dim TempLock As New Object()
            Parallel.For(0, Height,
            Function() As Double()
                If BufferA Is Nothing Then BufferA = New Double(3) {}
                Array.Clear(BufferA, 0, 4)
                Return BufferA
            End Function,
            Function(y, LoopState, LocalErrorSums)
                Dim RowOffset As Integer = y * Stride
                For x As Integer = 0 To Width - 1
                    Dim px As Integer = RowOffset + (x * 4)
                    Dim AlphaWeight As Double = ImageBytes1(px + 3) / 255.0
                    For Channel As Integer = 0 To 2
                        Dim PixDiff As Double = (CDbl(ImageBytes1(px + Channel)) - CDbl(ImageBytes2(px + Channel))) * AlphaWeight
                        LocalErrorSums(Channel) += (PixDiff * PixDiff)
                    Next
                    Dim AlphaDiff As Double = CDbl(ImageBytes1(px + 3)) - CDbl(ImageBytes2(px + 3))
                    LocalErrorSums(3) += (AlphaDiff * AlphaDiff)
                Next
                Return LocalErrorSums
            End Function,
            Sub(LocalErrorSums)
                SyncLock TempLock
                    For Channel As Integer = 0 To 3
                        ErrorSums(Channel) += LocalErrorSums(Channel)
                    Next
                End SyncLock
            End Sub)
            Dim PixCount As Double = Width * Height
            Dim Result As New ChannelMetric With {
                .B = ErrorSums(0) / PixCount,
                .G = ErrorSums(1) / PixCount,
                .R = ErrorSums(2) / PixCount,
                .A = ErrorSums(3) / PixCount}
            Result.Average = (Result.R + Result.G + Result.B) / 3.0
            Me.MSE = Result
            Me.PSNR = New ChannelMetric With {
                .R = CalculateChannelPSNR(Result.R),
                .G = CalculateChannelPSNR(Result.G),
                .B = CalculateChannelPSNR(Result.B),
                .A = CalculateChannelPSNR(Result.A),
                .Average = CalculateChannelPSNR(Result.Average)}
            MSE_Done = True
            If SSIM_Done Then
                ImageBytes1 = Nothing
                ImageBytes2 = Nothing
            End If
        End If
    End Sub

    Private Function CalculateChannelPSNR(ValMSE As Double) As Double
        If ValMSE = 0 Then Return 128
        Return 10 * Math.Log10((255.0 * 255.0) / ValMSE)
    End Function

    Public Sub CalcSSIM()
        If Not SSIM_Done Then
            Dim ErrorBlockSums(3) As Double
            Dim TempLock As New Object()
            Dim HeightBlocks As Integer = Height \ 8
            Dim WidthBlocks As Integer = Width \ 8
            Parallel.For(0, HeightBlocks,
            Function() As Double()
                If BufferA Is Nothing Then BufferA = New Double(3) {}
                Array.Clear(BufferA, 0, 4)
                Return BufferA
            End Function,
            Function(hBlock, LoopState, LocalErrorSums)
                Dim y As Integer = hBlock * 8
                For wBlock As Integer = 0 To WidthBlocks - 1
                    Dim x As Integer = wBlock * 8
                    For Channel As Integer = 0 To 3
                        LocalErrorSums(Channel) += CalculateBlockSSIM(x, y, Channel)
                    Next
                Next
                Return LocalErrorSums
            End Function,
            Sub(LocalErrorSums)
                SyncLock TempLock
                    For Channel As Integer = 0 To 3
                        ErrorBlockSums(Channel) += LocalErrorSums(Channel)
                    Next
                End SyncLock
            End Sub)
            Dim TotalBlocks As Double = HeightBlocks * WidthBlocks
            Dim Result As New ChannelMetric With {
                .B = ErrorBlockSums(0) / TotalBlocks,
                .G = ErrorBlockSums(1) / TotalBlocks,
                .R = ErrorBlockSums(2) / TotalBlocks,
                .A = ErrorBlockSums(3) / TotalBlocks}
            Result.Average = (Result.R + Result.G + Result.B) / 3.0
            Me.SSIM = Result
            SSIM_Done = True
            If MSE_Done Then
                ImageBytes1 = Nothing
                ImageBytes2 = Nothing
            End If
        End If
    End Sub

    Private Function CalculateBlockSSIM(xStart As Integer, yStart As Integer, Channel As Integer) As Double
        Dim Mu1 As Double = 0
        Dim Mu2 As Double = 0
        Dim Sigma1_1 As Double = 0
        Dim Sigma2_2 As Double = 0
        Dim Sigma1_2 As Double = 0
        Dim n As Integer = 64
        Dim IsColorChannel As Boolean = (Channel < 3)
        For y As Integer = yStart To yStart + 7
            Dim Offset = y * Stride
            For x As Integer = xStart To xStart + 7
                Dim px = Offset + (x * 4)
                Dim val1 As Double = ImageBytes1(px + Channel)
                Dim val2 As Double = ImageBytes2(px + Channel)
                If IsColorChannel Then
                    Dim AlphaWeight As Double = ImageBytes1(px + 3) / 255.0
                    val1 *= AlphaWeight
                    val2 *= AlphaWeight
                End If
                Mu1 += val1
                Mu2 += val2
            Next
        Next
        Mu1 /= n
        Mu2 /= n
        For y As Integer = yStart To yStart + 7
            Dim Offset = y * Stride
            For x As Integer = xStart To xStart + 7
                Dim px = Offset + (x * 4)
                Dim val1 As Double = ImageBytes1(px + Channel)
                Dim val2 As Double = ImageBytes2(px + Channel)
                If IsColorChannel Then
                    Dim AlphaWeight As Double = ImageBytes1(px + 3) / 255.0
                    val1 *= AlphaWeight
                    val2 *= AlphaWeight
                End If
                Dim Diff1 As Double = val1 - Mu1
                Dim Diff2 As Double = val2 - Mu2
                Sigma1_1 += Diff1 * Diff1
                Sigma2_2 += Diff2 * Diff2
                Sigma1_2 += Diff1 * Diff2
            Next
        Next
        Sigma1_1 /= (n - 1)
        Sigma2_2 /= (n - 1)
        Sigma1_2 /= (n - 1)
        Return ((2 * Mu1 * Mu2 + C1) * (2 * Sigma1_2 + C2)) / ((Mu1 * Mu1 + Mu2 * Mu2 + C1) * (Sigma1_1 + Sigma2_2 + C2))
    End Function

    Protected Overridable Sub Dispose(Disposing As Boolean)
        If Not Disposed Then
            ImageBytes1 = Nothing
            ImageBytes2 = Nothing
        End If
        Disposed = True
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose(True)
        GC.SuppressFinalize(Me)
    End Sub

End Class
