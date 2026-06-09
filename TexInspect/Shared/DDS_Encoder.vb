' DDS Encoder Class by WalkerMx
' Based on the documentation found here:
' http://doc.51windows.net/directx9_sdk/graphics/reference/DDSFileReference/ddsfileformat.htm
' https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dx-graphics-dds

Imports System.IO
Imports System.Threading

Public Class DDS_Encoder
    Implements IDisposable

    Public Disposed As Boolean

    Public Signature As String
    Public HeaderSize As Integer
    Public SurfaceFlags As DDS_SurfaceFlags
    Public Height As Integer
    Public Width As Integer
    Public PitchLinearSize As Integer
    Public Depth As Integer
    Public MipMapCount As Integer

    Public SubHeaderSize As Integer
    Public PixelFlags As DDS_PixelFlags
    Public FourCC As String
    Public RGBBitCount As Integer

    Public RedBitMask As Byte()
    Public GreenBitMask As Byte()
    Public BlueBitMask As Byte()
    Public AlphaBitMask As Byte()

    Public Caps1 As DDS_Caps1
    Public Caps2 As DDS_Caps2

    Public DXGIFormat As DXGI_Format
    Public ResourceDimension As DX10_ResourceDimension
    Public MiscFlag As DX10_MiscFlags
    Public ArraySize As Integer
    Public MiscFlags2 As DX10_MiscFlags2

    Private HasAlpha As Boolean
    Private HasPAlpha As Boolean
    Private HasNormal As Boolean
    Private HasMipMaps As Boolean
    Private HasCompression As Boolean
    Private HasExtendedHeader As Boolean

    Private MipCount As Integer
    Private BytesPerBlock As Integer
    Private CompressionMode As Integer
    Private InputFormat As PixelFormat = PixelFormats.Bgra32

    Private CubeFaces As String()

    Private HeaderBytes As Byte()
    Private WorkingBytes As Byte()
    Private PayloadBytes As Byte()

    <ThreadStatic> Private Shared BufferA As Integer()
    <ThreadStatic> Private Shared BufferB As Integer()
    <ThreadStatic> Private Shared BufferC As Integer()
    <ThreadStatic> Private Shared BufferD As Integer()
    <ThreadStatic> Private Shared BufferE As Integer()
    <ThreadStatic> Private Shared BufferF As Integer()

    ''' <summary>
    ''' Creates a DDS Image from a file on the disk.
    ''' </summary>
    ''' <param name="Source">Image file to create DDS from.</param>
    ''' <param name="Format">The explicit DXGI format to encode to.</param>
    ''' <param name="MipMaps">Create mipmaps for distant objects.</param>
    ''' <param name="LegacySupport">If true, strips the DX10 header and uses standard FourCC/Bitmasks. Throws an exception if the format requires DX10.</param>
    Public Sub New(Source As String, Format As DXGI_Format, MipMaps As Boolean, Optional LegacySupport As Boolean = False, Optional SpecialFlags As DDS_SpecialFlags = 0)
        HasMipMaps = MipMaps
        DXGIFormat = Format
        HasExtendedHeader = Not LegacySupport
        Dim TargetFormat As PixelFormat = PixelFormats.Bgra32
        Select Case SpecialFlags
            Case DDS_SpecialFlags.DDS_DXT2, DDS_SpecialFlags.DDS_DXT4
                HasPAlpha = True
                TargetFormat = PixelFormats.Pbgra32
            Case DDS_SpecialFlags.DDS_DXT5n, DDS_SpecialFlags.DDS_BC7n
                HasNormal = True
        End Select
        InputFormat = TargetFormat
        Dim FileUri As New Uri(Path.GetFullPath(Source))
        Dim Decoder As BitmapDecoder = BitmapDecoder.Create(FileUri, BitmapCreateOptions.None, BitmapCacheOption.None)
        Dim Frame As BitmapFrame = Decoder.Frames(0)
        Width = Frame.PixelWidth
        Height = Frame.PixelHeight
        HasAlpha = WicAlphaFormats.Contains(Frame.Format)
        Dim Stride As Integer = (Width * TargetFormat.BitsPerPixel + 7) \ 8
        WorkingBytes = New Byte(Stride * Height - 1) {}
        If Frame.Format = TargetFormat Then
            Frame.CopyPixels(WorkingBytes, Stride, 0)
        Else
            Dim ConvertedBmp As New FormatConvertedBitmap()
            ConvertedBmp.BeginInit()
            ConvertedBmp.Source = Frame
            ConvertedBmp.DestinationFormat = TargetFormat
            ConvertedBmp.EndInit()
            ConvertedBmp.CopyPixels(WorkingBytes, Stride, 0)
        End If
        If SpecialFlags = DDS_SpecialFlags.DDS_DXT1o Then HasAlpha = False
        InitializeHeaderValues()
        WriteHeader()
    End Sub

    ''' <summary>
    ''' Creates a DDS Image using an array of bytes, and the Width and Height.
    ''' </summary>
    ''' <param name="Source">Byte array to encode to DDS.  Must be 32BBP BGRA.</param>
    ''' <param name="ImageWidth">Width if the Image.</param>
    ''' <param name="ImageHeight">Height of the Image.</param>
    ''' <param name="Format">The explicit DXGI format to encode to.</param>
    ''' <param name="MipMaps">Create mipmaps for distant objects.</param>
    ''' <param name="LegacySupport">If true, strips the DX10 header and uses standard FourCC/Bitmasks. Throws an exception if the format requires DX10.</param>
    Public Sub New(Source As Byte(), ImageWidth As Integer, ImageHeight As Integer, Format As DXGI_Format, MipMaps As Boolean, Optional LegacySupport As Boolean = False)
        HasMipMaps = MipMaps
        DXGIFormat = Format
        Width = ImageWidth
        Height = ImageHeight
        HasAlpha = True
        WorkingBytes = Source
        InitializeHeaderValues()
        WriteHeader()
    End Sub

    ''' <summary>
    ''' Creates a DDS CubeMap from an array of files on the disk.
    ''' Expected face order in the Sources array: +X (Right), -X (Left), +Y (Top), -Y (Bottom), +Z (Front), -Z (Back).
    ''' </summary>
    ''' <param name="Sources">Array of 6 image paths corresponding to the cubemap faces.</param>
    ''' <param name="Format">The explicit DXGI format to encode to.</param>
    ''' <param name="MipMaps">Create mipmaps for distant objects.</param>
    ''' <param name="LegacySupport">If true, strips the DX10 header and uses standard FourCC/Bitmasks. Throws an exception if the format requires DX10.</param>
    Public Sub New(Sources As String(), Format As DXGI_Format, MipMaps As Boolean, Optional LegacySupport As Boolean = False)
        Me.New(Sources(0), Format, MipMaps, LegacySupport)
        If Sources.Length <> 6 Then
            Throw New ArgumentException("A cubemap requires exactly 6 image faces.")
        End If
        CubeFaces = Sources
        Caps1 = Caps1 Or DDS_Caps1.DDSCAPS_COMPLEX
        Caps2 = Caps2 Or DDS_Caps2.DDSCAPS2_CUBEMAP
        Caps2 = Caps2 Or DDS_Caps2.DDSCAPS2_CUBEMAP_POSITIVEX
        Caps2 = Caps2 Or DDS_Caps2.DDSCAPS2_CUBEMAP_NEGATIVEX
        Caps2 = Caps2 Or DDS_Caps2.DDSCAPS2_CUBEMAP_POSITIVEY
        Caps2 = Caps2 Or DDS_Caps2.DDSCAPS2_CUBEMAP_NEGATIVEY
        Caps2 = Caps2 Or DDS_Caps2.DDSCAPS2_CUBEMAP_POSITIVEZ
        Caps2 = Caps2 Or DDS_Caps2.DDSCAPS2_CUBEMAP_NEGATIVEZ
        If HasExtendedHeader Then
            MiscFlag = DX10_MiscFlags.D3D10_RESOURCE_MISC_TEXTURECUBE
        End If
        InitializeHeaderValues()
        WriteHeader()
    End Sub

    Private Sub InitializeHeaderValues()
        ResourceDimension = DX10_ResourceDimension.D3D10_RESOURCE_DIMENSION_TEXTURE2D
        RedBitMask = {0, 0, 0, 0}
        GreenBitMask = {0, 0, 0, 0}
        BlueBitMask = {0, 0, 0, 0}
        AlphaBitMask = {0, 0, 0, 0}
        Dim DynamicAlpha As DX10_MiscFlags2 = If(HasAlpha, If(HasPAlpha, DX10_MiscFlags2.DDS_ALPHA_MODE_PREMULTIPLIED, DX10_MiscFlags2.DDS_ALPHA_MODE_STRAIGHT), DX10_MiscFlags2.DDS_ALPHA_MODE_OPAQUE)
        Select Case DXGIFormat
            Case &H46, &H47, &H48 ' BC1 Typeless, UNORM, SRGB
                HasCompression = True
                CompressionMode = If(HasAlpha, 1, 0)
                BytesPerBlock = 8
                FourCC = "DXT1"
                MiscFlags2 = DynamicAlpha
            Case &H49, &H4A, &H4B ' BC2 Typeless, UNORM, SRGB
                HasCompression = True
                CompressionMode = 2
                BytesPerBlock = 16
                FourCC = If(HasPAlpha, "DXT2", "DXT3")
                MiscFlags2 = DynamicAlpha
            Case &H4C, &H4D, &H4E ' BC3 Typeless, UNORM, SRGB
                HasCompression = True
                CompressionMode = If(HasNormal, 30, 3)
                BytesPerBlock = 16
                FourCC = If(HasPAlpha, "DXT4", "DXT5")
                MiscFlags2 = DynamicAlpha
            Case &H4F, &H50 ' BC4 Typeless, UNORM
                HasCompression = True
                CompressionMode = 4
                BytesPerBlock = 8
                FourCC = "ATI1"
                MiscFlags2 = DX10_MiscFlags2.DDS_ALPHA_MODE_OPAQUE
            Case &H52, &H53 ' BC5 Typeless, UNORM
                HasCompression = True
                CompressionMode = 5
                BytesPerBlock = 16
                FourCC = "ATI2"
                MiscFlags2 = DX10_MiscFlags2.DDS_ALPHA_MODE_OPAQUE
            Case &H61, &H62, &H63 ' BC7 Typeless, UNORM, SRGB
                If Not HasExtendedHeader Then
                    Throw New ArgumentException($"Invalid format: {DXGIFormat.ToString()}.")
                End If
                HasCompression = True
                CompressionMode = If(HasNormal, 70, 7)
                BytesPerBlock = 16
                MiscFlags2 = DynamicAlpha
            Case &H57, &H5A, &H5B ' B8G8R8A8 Typeless, UNORM, SRGB
                HasCompression = False
                CompressionMode = -1
                BytesPerBlock = 4
                RGBBitCount = 32
                FourCC = ""
                MiscFlags2 = DynamicAlpha
                If Not HasExtendedHeader Then
                    RedBitMask = {0, 0, &HFF, 0}
                    GreenBitMask = {0, &HFF, 0, 0}
                    BlueBitMask = {&HFF, 0, 0, 0}
                    AlphaBitMask = {0, 0, 0, &HFF}
                End If
            Case &H58, &H5C, &H5D ' B8G8R8X8 Typeless, UNORM, SRGB
                HasCompression = False
                CompressionMode = -1
                BytesPerBlock = 4
                RGBBitCount = 32
                FourCC = ""
                MiscFlags2 = DX10_MiscFlags2.DDS_ALPHA_MODE_OPAQUE
                If Not HasExtendedHeader Then
                    RedBitMask = {0, 0, &HFF, 0}
                    GreenBitMask = {0, &HFF, 0, 0}
                    BlueBitMask = {&HFF, 0, 0, 0}
                End If
        End Select
        If Not HasExtendedHeader Then
            If HasCompression Then
                PixelFlags = DDS_PixelFlags.DDPF_FOURCC
                RGBBitCount = 0
            Else
                PixelFlags = DDS_PixelFlags.DDPF_RGB
            End If
            If HasAlpha AndAlso DXGIFormat <> DXGI_Format.DXGI_FORMAT_B8G8R8X8_UNORM Then
                PixelFlags = PixelFlags Or DDS_PixelFlags.DDPF_ALPHAPIXELS
            End If
        Else
            PixelFlags = DDS_PixelFlags.DDPF_FOURCC
            FourCC = "DX10"
            RGBBitCount = 0
        End If
        SurfaceFlags = DDS_SurfaceFlags.DDSD_CAPS Or DDS_SurfaceFlags.DDSD_PIXELFORMAT Or DDS_SurfaceFlags.DDSD_WIDTH Or DDS_SurfaceFlags.DDSD_HEIGHT
        Caps1 = DDS_Caps1.DDSCAPS_TEXTURE
        If HasMipMaps Then
            MipCount = CalcMips(Width, Height)
            SurfaceFlags = SurfaceFlags Or DDS_SurfaceFlags.DDSD_MIPMAPCOUNT
            Caps1 = Caps1 Or DDS_Caps1.DDSCAPS_COMPLEX Or DDS_Caps1.DDSCAPS_MIPMAP
        End If
        If HasCompression Then
            SurfaceFlags = SurfaceFlags Or DDS_SurfaceFlags.DDSD_LINEARSIZE
            PitchLinearSize = Math.Max(1, ((Width + 3) \ 4)) * BytesPerBlock * Math.Max(1, ((Height + 3) \ 4))
        Else
            SurfaceFlags = SurfaceFlags Or DDS_SurfaceFlags.DDSD_PITCH
            PitchLinearSize = Width * BytesPerBlock
        End If
    End Sub

    Private Sub WriteHeader()
        Using HeaderStream As New MemoryStream()

            HeaderStream.Write(OrderBytes("DDS "), 0, 4)                    ' dwMagic
            HeaderStream.Write(OrderBytes(124), 0, 4)                       ' dwSize
            HeaderStream.Write(OrderBytes(SurfaceFlags), 0, 4)              ' dwFlags
            HeaderStream.Write(OrderBytes(Height), 0, 4)                    ' dwHeight
            HeaderStream.Write(OrderBytes(Width), 0, 4)                     ' dwWidth
            HeaderStream.Write(OrderBytes(PitchLinearSize), 0, 4)           ' dwPitchOrLinearSize
            HeaderStream.Write(OrderBytes(0), 0, 4)                         ' dwDepth
            HeaderStream.Write(OrderBytes(MipCount), 0, 4)                  ' dwMipMapCount

            HeaderStream.Write(New Byte(43) {}, 0, 44)                      ' dwReserved1 x11

            HeaderStream.Write(OrderBytes(32), 0, 4)                        ' DDPIXELFORMAT dwSize
            HeaderStream.Write(OrderBytes(PixelFlags), 0, 4)                ' DDPIXELFORMAT dwFlags
            HeaderStream.Write(OrderBytes(FourCC), 0, 4)                    ' DDPIXELFORMAT dwFourCC
            HeaderStream.Write(OrderBytes(RGBBitCount), 0, 4)               ' DDPIXELFORMAT dwRGBBitCount
            HeaderStream.Write(RedBitMask, 0, 4)                            ' DDPIXELFORMAT dwRBitMask
            HeaderStream.Write(GreenBitMask, 0, 4)                          ' DDPIXELFORMAT dwGBitMask
            HeaderStream.Write(BlueBitMask, 0, 4)                           ' DDPIXELFORMAT dwBBitMask
            HeaderStream.Write(AlphaBitMask, 0, 4)                          ' DDPIXELFORMAT dwABitMask

            HeaderStream.Write(OrderBytes(Caps1), 0, 4)                     ' dwCaps1
            HeaderStream.Write(OrderBytes(Caps2), 0, 4)                     ' dwCaps2

            HeaderStream.Write(New Byte(11) {}, 0, 12)                      ' dwCaps3, dwCaps4, dwReserved2

            If HasExtendedHeader Then
                HeaderStream.Write(OrderBytes(DXGIFormat), 0, 4)            ' dwDxgiFormat
                HeaderStream.Write(OrderBytes(ResourceDimension), 0, 4)     ' dwResourceDimension
                HeaderStream.Write(OrderBytes(MiscFlag), 0, 4)              ' dwMiscFlag
                HeaderStream.Write(OrderBytes(1), 0, 4)                     ' dwArraySize
                HeaderStream.Write(OrderBytes(MiscFlags2), 0, 4)            ' dwMiscFlags2
            End If

            HeaderBytes = HeaderStream.ToArray

        End Using
    End Sub

    Public Sub BeginEncode()
        Dim TempWidth As Integer = Width
        Dim TempHeight As Integer = Height
        Using PayloadStream As New MemoryStream()
            PayloadStream.Write(HeaderBytes, 0, HeaderBytes.Length)
            Dim NextBytes As Byte() = GetImageData(WorkingBytes, TempWidth, TempHeight)
            PayloadStream.Write(NextBytes, 0, NextBytes.Length)
            If HasMipMaps Then
                For i = 0 To MipCount - 2
                    WorkingBytes = HalveArray(WorkingBytes, TempWidth, TempHeight)
                    TempWidth = Math.Max(1, TempWidth >> 1)
                    TempHeight = Math.Max(1, TempHeight >> 1)
                    NextBytes = GetImageData(WorkingBytes, TempWidth, TempHeight)
                    PayloadStream.Write(NextBytes, 0, NextBytes.Length)
                Next
            End If
            WorkingBytes = Nothing
            PayloadBytes = PayloadStream.ToArray
        End Using
    End Sub

    Public Sub BeginEncodeCube()
        Using PayloadStream As New MemoryStream()
            PayloadStream.Write(HeaderBytes, 0, HeaderBytes.Length)
            For faceIndex As Integer = 0 To 5
                Dim TempWidth As Integer = Me.Width
                Dim TempHeight As Integer = Me.Height
                Dim FileUri As New Uri(Path.GetFullPath(CubeFaces(faceIndex)))
                Dim Decoder As BitmapDecoder = BitmapDecoder.Create(FileUri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad)
                Dim Frame As BitmapFrame = Decoder.Frames(0)
                If Frame.PixelWidth <> Me.Width OrElse Frame.PixelHeight <> Me.Height Then
                    Throw New InvalidDataException($"Dimensions of face {faceIndex} do not match the base (+X) image.")
                End If
                Dim ConvertedBmp As New FormatConvertedBitmap()
                ConvertedBmp.BeginInit()
                ConvertedBmp.Source = Frame
                ConvertedBmp.DestinationFormat = InputFormat
                ConvertedBmp.EndInit()
                Dim Stride As Integer = (Me.Width * InputFormat.BitsPerPixel + 7) \ 8
                WorkingBytes = New Byte(Stride * Me.Height - 1) {}
                ConvertedBmp.CopyPixels(WorkingBytes, Stride, 0)
                Dim NextBytes As Byte() = GetImageData(WorkingBytes, TempWidth, TempHeight)
                PayloadStream.Write(NextBytes, 0, NextBytes.Length)
                If HasMipMaps Then
                    For i As Integer = 0 To MipCount - 2
                        WorkingBytes = HalveArray(WorkingBytes, TempWidth, TempHeight)
                        TempWidth = Math.Max(1, TempWidth >> 1)
                        TempHeight = Math.Max(1, TempHeight >> 1)
                        NextBytes = GetImageData(WorkingBytes, TempWidth, TempHeight)
                        PayloadStream.Write(NextBytes, 0, NextBytes.Length)
                    Next
                End If
            Next
            WorkingBytes = Nothing
            PayloadBytes = PayloadStream.ToArray
        End Using
    End Sub

    Private Function GetImageData(BitmapBytes As Byte(), Width As Integer, Height As Integer) As Byte()
        If HasCompression Then
            Return BlockCompress(BitmapBytes, Width, Height)
        Else
            Return WriteUncompressed(BitmapBytes, HasAlpha)
        End If
    End Function

    Private Function WriteUncompressed(SourceData As Byte(), Alpha As Boolean) As Byte()
        If Alpha Then
            If MipCount > 1 Then
                Return DirectCast(SourceData.Clone(), Byte())
            Else
                Return SourceData
            End If
        End If
        Dim Result(SourceData.Length - 1) As Byte
        Buffer.BlockCopy(SourceData, 0, Result, 0, SourceData.Length)
        For i As Integer = 3 To Result.Length - 1 Step 4
            Result(i) = &HFF
        Next
        Return Result
    End Function

    Private Function BlockCompress(SourceData As Byte(), Width As Integer, Height As Integer) As Byte()
        Dim BlockWidth As Integer = Math.Max(1, (Width + 3) \ 4)
        Dim BlockHeight As Integer = Math.Max(1, (Height + 3) \ 4)
        Dim Result(BlockWidth * BlockHeight * BytesPerBlock - 1) As Byte
        Parallel.For(0, BlockHeight, Options, Sub(yBlock)
                                                  If BufferA Is Nothing Then
                                                      BufferA = New Integer(15) {} : BufferB = New Integer(15) {}
                                                      BufferC = New Integer(15) {} : BufferD = New Integer(15) {}
                                                      BufferE = New Integer(23) {} : BufferF = New Integer(15) {}
                                                  End If
                                                  Dim yPixelBase As Integer = yBlock * 4
                                                  Dim rowOutputOffset As Integer = yBlock * BlockWidth * BytesPerBlock
                                                  For xBlock As Integer = 0 To BlockWidth - 1
                                                      Dim xPixelBase As Integer = xBlock * 4
                                                      Dim currentBlockOffset As Integer = rowOutputOffset + (xBlock * BytesPerBlock)
                                                      Select Case CompressionMode
                                                          Case 0 ' BC1
                                                              EncodeBlockBC1(SourceData, xPixelBase, yPixelBase, Width, Height, Result, currentBlockOffset, BufferA, BufferB, BufferC, BufferD, BufferE, BufferF)
                                                          Case 1 ' BC1a
                                                              Dim AlphaMask As UShort = 0
                                                              EncodeBlockBC1(SourceData, xPixelBase, yPixelBase, Width, Height, Result, currentBlockOffset, BufferA, BufferB, BufferC, BufferD, BufferE, BufferF, AlphaMask)
                                                              If HasAlpha AndAlso AlphaMask > 0 Then
                                                                  EncodeBlockBC1a(Result, currentBlockOffset, BufferA, AlphaMask)
                                                              End If
                                                          Case 2 ' BC2
                                                              EncodeBlockBC2(SourceData, xPixelBase, yPixelBase, Width, Height, Result, currentBlockOffset)
                                                              EncodeBlockBC1(SourceData, xPixelBase, yPixelBase, Width, Height, Result, currentBlockOffset + 8, BufferA, BufferB, BufferC, BufferD, BufferE, BufferF)
                                                          Case 3 ' BC3
                                                              EncodeBlockBC3(SourceData, xPixelBase, yPixelBase, Width, Height, 3, Result, currentBlockOffset, BufferA, BufferB, BufferC)
                                                              EncodeBlockBC1(SourceData, xPixelBase, yPixelBase, Width, Height, Result, currentBlockOffset + 8, BufferA, BufferB, BufferC, BufferD, BufferE, BufferF)
                                                          Case 4 ' BC4
                                                              EncodeBlockBC3(SourceData, xPixelBase, yPixelBase, Width, Height, 2, Result, currentBlockOffset, BufferA, BufferB, BufferC)
                                                          Case 5 ' BC5
                                                              EncodeBlockBC3(SourceData, xPixelBase, yPixelBase, Width, Height, 2, Result, currentBlockOffset, BufferA, BufferB, BufferC)
                                                              EncodeBlockBC3(SourceData, xPixelBase, yPixelBase, Width, Height, 1, Result, currentBlockOffset + 8, BufferA, BufferB, BufferC)
                                                          Case 7 ' BC7 (Dynamic Mode 1, 6, 7)
                                                              EncodeBlockBC7(SourceData, xPixelBase, yPixelBase, Width, Height, Result, currentBlockOffset, BufferA, BufferB, BufferC, BufferD, BufferE, BufferF)
                                                          Case 30 ' DXT5n
                                                              EncodeBlockBC3(SourceData, xPixelBase, yPixelBase, Width, Height, 2, Result, currentBlockOffset, BufferA, BufferB, BufferC)
                                                              EncodeBlockBC1n(SourceData, xPixelBase, yPixelBase, Width, Height, Result, currentBlockOffset + 8, BufferA, 1)
                                                          Case 70 ' BC7n (Dynamic Mode 4, 5)
                                                              EncodeBlockBC7n(SourceData, xPixelBase, yPixelBase, Width, Height, Result, currentBlockOffset, BufferA, BufferB, BufferC, BufferD, BufferE, BufferF)
                                                      End Select
                                                  Next
                                              End Sub)
        Return Result
    End Function

    Private Sub EncodeBlockBC7n(SourceData As Byte(), xPixelBase As Integer, yPixelBase As Integer, Width As Integer, Height As Integer, Result As Byte(), OutputOffset As Integer, LocalR() As Integer, LocalG() As Integer, ColorEndpoints() As Integer, AlphaEndpoints() As Integer, ColorIndices() As Integer, AlphaIndices() As Integer)
        Dim minG As Integer = 255 : Dim maxG As Integer = 0
        Dim minR As Integer = 255 : Dim maxR As Integer = 0
        Dim sumR As Integer = 0 : Dim sumSqR As Integer = 0
        Dim sumG As Integer = 0 : Dim sumSqG As Integer = 0
        Dim LocalIndex As Integer = 0
        For j As Integer = 0 To 3
            Dim yPixel As Integer = Math.Min(yPixelBase + j, Height - 1)
            Dim RowInputOffset As Integer = yPixel * Width * 4
            For i As Integer = 0 To 3
                Dim xPixel As Integer = Math.Min(xPixelBase + i, Width - 1)
                Dim PixelIndex As Integer = RowInputOffset + (xPixel * 4)
                Dim rVal As Integer = SourceData(PixelIndex + 2)
                Dim gVal As Integer = SourceData(PixelIndex + 1)
                LocalR(LocalIndex) = rVal
                LocalG(LocalIndex) = gVal
                If gVal < minG Then minG = gVal
                If gVal > maxG Then maxG = gVal
                If rVal < minR Then minR = rVal
                If rVal > maxR Then maxR = rVal
                sumR += rVal
                sumSqR += (rVal * rVal)
                sumG += gVal
                sumSqG += (gVal * gVal)
                LocalIndex += 1
            Next
        Next
        Dim RangeG As Integer = maxG - minG
        Dim RangeR As Integer = maxR - minR
        Dim varR As Integer = ((sumSqR << 4) - (sumR * sumR))
        Dim varG As Integer = ((sumSqG << 4) - (sumG * sumG))
        If varR < 1500 AndAlso varG < 1500 Then
            If RangeG >= RangeR Then
                GetEndpoints1D(LocalG, 7, 2, ColorEndpoints, 2, ColorIndices, minG, maxG)
                GetEndpoints1D(LocalR, 8, 2, AlphaEndpoints, 0, AlphaIndices, minR, maxR)
                ColorEndpoints(0) = 127 : ColorEndpoints(1) = 127
                ColorEndpoints(4) = 127 : ColorEndpoints(5) = 127
                EncodeMode5(ColorEndpoints, AlphaEndpoints, ColorIndices, AlphaIndices, 1, Result, OutputOffset)
            Else
                GetEndpoints1D(LocalR, 7, 2, ColorEndpoints, 0, ColorIndices, minR, maxR)
                GetEndpoints1D(LocalG, 8, 2, AlphaEndpoints, 0, AlphaIndices, minG, maxG)
                ColorEndpoints(2) = 127 : ColorEndpoints(3) = 127
                ColorEndpoints(4) = 127 : ColorEndpoints(5) = 127
                EncodeMode5(ColorEndpoints, AlphaEndpoints, ColorIndices, AlphaIndices, 2, Result, OutputOffset)
            End If
        Else
            If RangeR >= RangeG Then
                GetEndpoints1D(LocalR, 6, 3, AlphaEndpoints, 0, AlphaIndices, minR, maxR)
                GetEndpoints1D(LocalG, 5, 2, ColorEndpoints, 2, ColorIndices, minG, maxG)
                ColorEndpoints(0) = 31 : ColorEndpoints(1) = 31
                ColorEndpoints(4) = 31 : ColorEndpoints(5) = 31
                EncodeMode4(ColorEndpoints, AlphaEndpoints, ColorIndices, AlphaIndices, 1, Result, OutputOffset)
            Else
                GetEndpoints1D(LocalG, 6, 3, AlphaEndpoints, 0, AlphaIndices, minG, maxG)
                GetEndpoints1D(LocalR, 5, 2, ColorEndpoints, 0, ColorIndices, minR, maxR)
                ColorEndpoints(2) = 31 : ColorEndpoints(3) = 31
                ColorEndpoints(4) = 31 : ColorEndpoints(5) = 31
                EncodeMode4(ColorEndpoints, AlphaEndpoints, ColorIndices, AlphaIndices, 2, Result, OutputOffset)
            End If
        End If
    End Sub

    Private Sub EncodeBlockBC7(SourceData As Byte(), xPixelBase As Integer, yPixelBase As Integer, Width As Integer, Height As Integer, Result As Byte(), OutputOffset As Integer, LocalB() As Integer, LocalG() As Integer, LocalR() As Integer, LocalA() As Integer, Endpoints() As Integer, Indices() As Integer)
        Dim LocalIndex As Integer = 0
        Dim MinA As Integer = 255, MaxA As Integer = 0
        Dim SumB As Integer = 0, SumSquareB As Integer = 0
        Dim SumG As Integer = 0, SumSquareG As Integer = 0
        Dim SumR As Integer = 0, SumSquareR As Integer = 0
        Dim MinLumIndex As Integer = 0, MaxLumIndex As Integer = 0
        Dim MinLum As Integer = 1000, MaxLum As Integer = -1
        Dim WidthBound As Integer = Width - 1
        Dim HeightBound As Integer = Height - 1
        Dim Stride As Integer = Width * 4
        For j As Integer = 0 To 3
            Dim yPixel As Integer = yPixelBase + j
            If yPixel > HeightBound Then yPixel = HeightBound
            Dim RowInputOffset As Integer = yPixel * Stride
            For i As Integer = 0 To 3
                Dim xPixel As Integer = xPixelBase + i
                If xPixel > WidthBound Then xPixel = WidthBound
                Dim PixelIndex As Integer = RowInputOffset + (xPixel << 2)
                Dim ValB As Integer = SourceData(PixelIndex)
                Dim ValG As Integer = SourceData(PixelIndex + 1)
                Dim ValR As Integer = SourceData(PixelIndex + 2)
                Dim ValA As Integer = SourceData(PixelIndex + 3)
                LocalB(LocalIndex) = ValB
                LocalG(LocalIndex) = ValG
                LocalR(LocalIndex) = ValR
                LocalA(LocalIndex) = ValA
                SumB += ValB : SumSquareB += (ValB * ValB)
                SumG += ValG : SumSquareG += (ValG * ValG)
                SumR += ValR : SumSquareR += (ValR * ValR)
                If ValA < MinA Then MinA = ValA
                If ValA > MaxA Then MaxA = ValA
                Dim Lum As Integer = ValR + ValG + ValB
                If Lum < MinLum Then
                    MinLum = Lum
                    MinLumIndex = LocalIndex
                End If
                If Lum > MaxLum Then
                    MaxLum = Lum
                    MaxLumIndex = LocalIndex
                End If
                LocalIndex += 1
            Next
        Next
        If MaxA = 0 Then
            Result(OutputOffset) = &H40
            Return
        End If
        Dim VarR As Integer = (SumSquareR << 4) - (SumR * SumR)
        Dim VarG As Integer = (SumSquareG << 4) - (SumG * SumG)
        Dim VarB As Integer = (SumSquareB << 4) - (SumB * SumB)
        Dim MaxVar As Integer = VarR
        If VarG > MaxVar Then MaxVar = VarG
        If VarB > MaxVar Then MaxVar = VarB
        Dim VarThreshold As Integer = 27750
        Dim UseMode1_7 As Boolean = False
        Dim PBits As Integer = 0
        If MaxVar >= VarThreshold Then
            Dim VectR As Integer = LocalR(MaxLumIndex) - LocalR(MinLumIndex)
            Dim VectG As Integer = LocalG(MaxLumIndex) - LocalG(MinLumIndex)
            Dim VectB As Integer = LocalB(MaxLumIndex) - LocalB(MinLumIndex)
            Dim VectMagnitudeSquare As Long = (VectR * VectR) + (VectG * VectG) + (VectB * VectB)
            If VectMagnitudeSquare > 0 Then
                Dim TotalCrossProductSquare As Long = 0
                For i As Integer = 0 To 15
                    Dim PVectR As Integer = LocalR(i) - LocalR(MinLumIndex)
                    Dim PVectG As Integer = LocalG(i) - LocalG(MinLumIndex)
                    Dim PVectB As Integer = LocalB(i) - LocalB(MinLumIndex)
                    Dim CrossProductR As Long = (PVectG * VectB) - (PVectB * VectG)
                    Dim CrossProductG As Long = (PVectB * VectR) - (PVectR * VectB)
                    Dim CrossProductB As Long = (PVectR * VectG) - (PVectG * VectR)
                    TotalCrossProductSquare += (CrossProductR * CrossProductR) + (CrossProductG * CrossProductG) + (CrossProductB * CrossProductB)
                Next
                Dim ColinearThreshold As Long = (VectMagnitudeSquare * VectMagnitudeSquare * 10L) >> 8
                If TotalCrossProductSquare >= ColinearThreshold Then
                    UseMode1_7 = True
                End If
            End If
        End If
        If UseMode1_7 Then
            Dim MaxDistance As Integer = 0
            Dim CornerAIndex As Integer = 0, CornerBIndex As Integer = 0
            Dim DiffR As Integer, DiffG As Integer, DiffB As Integer
            For i As Integer = 0 To 2
                For j As Integer = i + 1 To 3
                    Dim CornerA = BlockCorners(i)
                    Dim CornerB = BlockCorners(j)
                    DiffR = LocalR(CornerA) - LocalR(CornerB)
                    DiffG = LocalG(CornerA) - LocalG(CornerB)
                    DiffB = LocalB(CornerA) - LocalB(CornerB)
                    Dim RgbDistance As Integer = (DiffR * DiffR) + (DiffG * DiffG) + (DiffB * DiffB)
                    If RgbDistance > MaxDistance Then
                        MaxDistance = RgbDistance
                        CornerAIndex = CornerA
                        CornerBIndex = CornerB
                    End If
                Next
            Next
            Dim CornerAR = LocalR(CornerAIndex), CornerAG = LocalG(CornerAIndex), CornerAB = LocalB(CornerAIndex)
            Dim CornerBR = LocalR(CornerBIndex), CornerBG = LocalG(CornerBIndex), CornerBB = LocalB(CornerBIndex)
            Dim ShapeBits As Integer = 0
            For c As Integer = 0 To 3
                Dim CornerIndex = BlockCorners(c)
                DiffR = LocalR(CornerIndex) - CornerAR
                DiffG = LocalG(CornerIndex) - CornerAG
                DiffB = LocalB(CornerIndex) - CornerAB
                Dim DistanceToA As Integer = (DiffR * DiffR) + (DiffG * DiffG) + (DiffB * DiffB)
                DiffR = LocalR(CornerIndex) - CornerBR
                DiffG = LocalG(CornerIndex) - CornerBG
                DiffB = LocalB(CornerIndex) - CornerBB
                Dim DistanceToB As Integer = (DiffR * DiffR) + (DiffG * DiffG) + (DiffB * DiffB)
                Dim TargetSubset As Integer = If(DistanceToA < DistanceToB, 0, 1)
                ShapeBits = ShapeBits Or (TargetSubset << (3 - c))
            Next
            If (ShapeBits And 8) = 8 Then ShapeBits = (Not ShapeBits) And 15
            Dim BestIndex As Integer = PartitionMap(ShapeBits And 7)
            If BestIndex <> -1 Then
                Dim SubMask = PartitionTable2(BestIndex)
                If MinA = 255 Then
                    GetEndpointsPCA(SubMask, 0, LocalR, LocalG, LocalB, LocalA, 2, 7, 0, Endpoints, 0, Indices, PBits)
                    GetEndpointsPCA(SubMask, 1, LocalR, LocalG, LocalB, LocalA, 2, 7, 0, Endpoints, 8, Indices, PBits)
                    EncodeMode1(BestIndex, Endpoints, Indices, Result, OutputOffset, PBits)
                Else
                    GetEndpointsPCA(SubMask, 0, LocalR, LocalG, LocalB, LocalA, 3, 3, 1, Endpoints, 0, Indices, PBits)
                    GetEndpointsPCA(SubMask, 1, LocalR, LocalG, LocalB, LocalA, 3, 3, 1, Endpoints, 8, Indices, PBits)
                    EncodeMode7(BestIndex, Endpoints, Indices, Result, OutputOffset, PBits)
                End If
                Return
            End If
        End If
        GetEndpointsPCA(0, 0, LocalR, LocalG, LocalB, LocalA, 1, 15, 1, Endpoints, 0, Indices, PBits)
        EncodeMode6(Endpoints, Indices, Result, OutputOffset, PBits)
    End Sub

    Private Sub EncodeBlockBC3(SourceData As Byte(), xPixelBase As Integer, yPixelBase As Integer, Width As Integer, Height As Integer, ChannelOffset As Integer, Result As Byte(), OutputOffset As Integer, ChannelArray() As Integer, Endpoints() As Integer, Indices() As Integer)
        Dim minVal As Integer = 255
        Dim maxVal As Integer = 0
        Dim minRemaining As Integer = 255
        Dim maxRemaining As Integer = 0
        Dim idx As Integer = 0
        For j As Integer = 0 To 3
            Dim py As Integer = Math.Min(yPixelBase + j, Height - 1)
            Dim rowInputOffset As Integer = py * Width * 4
            For i As Integer = 0 To 3
                Dim px As Integer = Math.Min(xPixelBase + i, Width - 1)
                Dim pixelIdx As Integer = rowInputOffset + (px * 4)
                Dim v As Integer = SourceData(pixelIdx + ChannelOffset)
                ChannelArray(idx) = v
                If v < minVal Then minVal = v
                If v > maxVal Then maxVal = v
                If v > 0 AndAlso v < minRemaining Then minRemaining = v
                If v < 255 AndAlso v > maxRemaining Then maxRemaining = v
                idx += 1
            Next
        Next
        Dim useModeB As Boolean = (minVal = 0 AndAlso maxVal = 255 AndAlso maxRemaining >= minRemaining AndAlso (maxRemaining - minRemaining) <= 182)
        Dim Val0 As Byte
        Dim Val1 As Byte
        If useModeB Then
            GetEndpoints1D(ChannelArray, 8, 0, Endpoints, 0, Indices, minRemaining, maxRemaining)
            Val0 = CByte(Endpoints(0))
            Val1 = CByte(Endpoints(1))
            If Val0 = Val1 Then
                If Val0 > 0 Then Val0 -= 1 Else Val1 += 1
            End If
        Else
            GetEndpoints1D(ChannelArray, 8, 0, Endpoints, 0, Indices, minVal, maxVal)
            Val0 = CByte(Endpoints(1))
            Val1 = CByte(Endpoints(0))
            If Val0 = Val1 Then
                If Val0 > 0 Then Val1 -= 1 Else Val0 += 1
            End If
        End If
        Result(OutputOffset) = Val0
        Result(OutputOffset + 1) = Val1
        Dim BitBuffer As Long = 0
        Dim Intervals As Integer = If(useModeB, 5, 7)
        Dim Diff As Integer = CInt(Val0) - Val1
        Dim Mask As Integer = Diff >> 31
        Dim Range As Integer = (Diff Xor Mask) - Mask
        Dim HalfRange As Integer = Range \ 2
        For i As Integer = 0 To 15
            Dim v As Integer = ChannelArray(i)
            Dim Index As Long
            If useModeB AndAlso v = 0 Then
                Index = 6
            ElseIf useModeB AndAlso v = 255 Then
                Index = 7
            Else
                Diff = v - CInt(Val0)
                Mask = Diff >> 31
                Dim distance As Integer = (Diff Xor Mask) - Mask
                If distance <= 0 Then
                    Index = 0
                ElseIf distance >= Range Then
                    Index = 1
                Else
                    Dim stepCount As Integer = (distance * Intervals + HalfRange) \ Range
                    If stepCount <= 0 Then
                        Index = 0
                    ElseIf stepCount >= Intervals Then
                        Index = 1
                    Else
                        Index = stepCount + 1
                    End If
                End If
            End If
            BitBuffer = BitBuffer Or (Index << (i * 3))
        Next
        Dim ByteOffset As Integer = OutputOffset + 2
        Result(ByteOffset) = CByte(BitBuffer And &HFF)
        Result(ByteOffset + 1) = CByte((BitBuffer >> 8) And &HFF)
        Result(ByteOffset + 2) = CByte((BitBuffer >> 16) And &HFF)
        Result(ByteOffset + 3) = CByte((BitBuffer >> 24) And &HFF)
        Result(ByteOffset + 4) = CByte((BitBuffer >> 32) And &HFF)
        Result(ByteOffset + 5) = CByte((BitBuffer >> 40) And &HFF)
    End Sub

    Private Sub EncodeBlockBC2(SourceData As Byte(), xPixelBase As Integer, yPixelBase As Integer, Width As Integer, Height As Integer, Result As Byte(), OutputOffset As Integer)
        Dim byteIdx As Integer = 0
        For j As Integer = 0 To 3
            Dim py As Integer = Math.Min(yPixelBase + j, Height - 1)
            Dim rowInputOffset As Integer = py * Width * 4
            For i As Integer = 0 To 3 Step 2
                Dim px0 As Integer = Math.Min(xPixelBase + i, Width - 1)
                Dim alpha0 As Byte = SourceData(rowInputOffset + (px0 * 4) + 3)
                Dim nibble0 As Byte = CByte((CInt(alpha0) + 8) \ 17)
                Dim px1 As Integer = Math.Min(xPixelBase + i + 1, Width - 1)
                Dim alpha1 As Byte = SourceData(rowInputOffset + (px1 * 4) + 3)
                Dim nibble1 As Byte = CByte((CInt(alpha1) + 8) \ 17)
                Result(OutputOffset + byteIdx) = CByte(nibble0 Or (nibble1 << 4))
                byteIdx += 1
            Next
        Next
    End Sub

    Private Sub EncodeBlockBC1n(SourceData As Byte(), xPixelBase As Integer, yPixelBase As Integer, Width As Integer, Height As Integer, Result As Byte(), OutputOffset As Integer, LocalBuffer() As Integer, ChannelOffset As Integer)
        Dim minVal As Integer = 255
        Dim maxVal As Integer = 0
        Dim isEdge As Boolean = (xPixelBase + 4 > Width) Or (yPixelBase + 4 > Height)
        Dim idx As Integer = 0
        For j As Integer = 0 To 3
            Dim yPixel As Integer = If(isEdge, Math.Min(yPixelBase + j, Height - 1), yPixelBase + j)
            Dim rowOffset As Integer = yPixel * Width * 4
            For i As Integer = 0 To 3
                Dim xPixel As Integer = If(isEdge, Math.Min(xPixelBase + i, Width - 1), xPixelBase + i)
                Dim val As Integer = SourceData(rowOffset + (xPixel * 4) + ChannelOffset)
                LocalBuffer(idx) = val
                If val < minVal Then minVal = val
                If val > maxVal Then maxVal = val
                idx += 1
            Next
        Next
        Dim g0 As Integer = maxVal >> 2
        Dim g1 As Integer = minVal >> 2
        Dim col0 As UShort = CUShort(g0 << 5)
        Dim col1 As UShort = CUShort(g1 << 5)
        If g0 = g1 Then
            Result(OutputOffset) = CByte(col0 And &HFF)
            Result(OutputOffset + 1) = CByte(col0 >> 8)
            Result(OutputOffset + 2) = CByte(col0 And &HFF)
            Result(OutputOffset + 3) = CByte(col0 >> 8)
            Return
        End If
        Dim ColorTable As UInteger = 0
        Dim g0_8 As Integer = (g0 << 2) Or (g0 >> 4)
        Dim g1_8 As Integer = (g1 << 2) Or (g1 >> 4)
        Dim range8 As Integer = g0_8 - g1_8
        If range8 > 0 Then
            For i As Integer = 0 To 15
                Dim scaledDist As Integer = ((LocalBuffer(i) - g1_8) * 3 + (range8 >> 1)) \ range8
                If scaledDist < 0 Then
                    scaledDist = 0
                ElseIf scaledDist > 3 Then
                    scaledDist = 3
                End If
                Dim index As UInteger = CUInt((&H231 >> (scaledDist << 2)) And 3)
                ColorTable = ColorTable Or (index << (i << 1))
            Next
        End If
        Result(OutputOffset) = CByte(col0 And &HFF)
        Result(OutputOffset + 1) = CByte(col0 >> 8)
        Result(OutputOffset + 2) = CByte(col1 And &HFF)
        Result(OutputOffset + 3) = CByte(col1 >> 8)
        Result(OutputOffset + 4) = CByte(ColorTable And &HFF)
        Result(OutputOffset + 5) = CByte((ColorTable >> 8) And &HFF)
        Result(OutputOffset + 6) = CByte((ColorTable >> 16) And &HFF)
        Result(OutputOffset + 7) = CByte((ColorTable >> 24) And &HFF)
    End Sub

    Private Sub EncodeBlockBC1a(Result As Byte(), OutputOffset As Integer, PixelArray() As Integer, AlphaMask As UShort)
        If AlphaMask = 0 Then Return
        Dim Col0 As UShort = Result(OutputOffset) Or (CUShort(Result(OutputOffset + 1)) << 8)
        Dim Col1 As UShort = Result(OutputOffset + 2) Or (CUShort(Result(OutputOffset + 3)) << 8)
        Dim temp As UShort = Col0 : Col0 = Col1 : Col1 = temp
        Result(OutputOffset) = CByte(Col0 And &HFF)
        Result(OutputOffset + 1) = CByte(Col0 >> 8)
        Result(OutputOffset + 2) = CByte(Col1 And &HFF)
        Result(OutputOffset + 3) = CByte(Col1 >> 8)
        Dim R0 As Integer = (Col0 >> 8) And &HF8 : R0 = R0 Or (R0 >> 5)
        Dim G0 As Integer = (Col0 >> 3) And &HFC : G0 = G0 Or (G0 >> 6)
        Dim B0 As Integer = (Col0 << 3) And &HF8 : B0 = B0 Or (B0 >> 5)
        Dim R1 As Integer = (Col1 >> 8) And &HF8 : R1 = R1 Or (R1 >> 5)
        Dim G1 As Integer = (Col1 >> 3) And &HFC : G1 = G1 Or (G1 >> 6)
        Dim B1 As Integer = (Col1 << 3) And &HF8 : B1 = B1 Or (B1 >> 5)
        Dim R2 As Integer = (R0 + R1 + 1) \ 2
        Dim G2 As Integer = (G0 + G1 + 1) \ 2
        Dim B2 As Integer = (B0 + B1 + 1) \ 2
        Dim ColorTable As UInteger = 0
        Dim shift As Integer = 0
        For i As Integer = 0 To 15
            Dim Index As UInteger = 0
            If (AlphaMask And (1US << i)) <> 0 Then
                Index = 3
            Else
                Dim OrigPix As Integer = PixelArray(i)
                Dim PixR As Integer = (OrigPix >> 16) And &HFF
                Dim PixG As Integer = (OrigPix >> 8) And &HFF
                Dim PixB As Integer = OrigPix And &HFF
                Dim dR As Integer = PixR - R0 : Dim dG As Integer = PixG - G0 : Dim dB As Integer = PixB - B0
                Dim minErr As Integer = (dR * dR * 3) + (dG * dG * 4) + (dB * dB * 2)
                Index = 0
                dR = PixR - R1 : dG = PixG - G1 : dB = PixB - B1
                Dim err As Integer = (dR * dR * 3) + (dG * dG * 4) + (dB * dB * 2)
                If err < minErr Then
                    minErr = err : Index = 1
                End If
                dR = PixR - R2 : dG = PixG - G2 : dB = PixB - B2
                err = (dR * dR * 3) + (dG * dG * 4) + (dB * dB * 2)
                If err < minErr Then
                    Index = 2
                End If
            End If
            ColorTable = ColorTable Or (Index << shift)
            shift += 2
        Next
        Result(OutputOffset + 4) = CByte(ColorTable And &HFF)
        Result(OutputOffset + 5) = CByte((ColorTable >> 8) And &HFF)
        Result(OutputOffset + 6) = CByte((ColorTable >> 16) And &HFF)
        Result(OutputOffset + 7) = CByte((ColorTable >> 24) And &HFF)
    End Sub

    Private Sub EncodeBlockBC1(SourceData As Byte(), xPixelBase As Integer, yPixelBase As Integer, Width As Integer, Height As Integer, Result As Byte(), OutputOffset As Integer, LocalB() As Integer, LocalG() As Integer, LocalR() As Integer, LocalA() As Integer, Endpoints() As Integer, Indices() As Integer, Optional ByRef AlphaMask As UShort = 0)
        Dim MaxY As Integer = Height - 1
        Dim MaxX As Integer = Width - 1
        Dim idx As Integer = 0
        AlphaMask = 0
        For j As Integer = 0 To 3
            Dim py As Integer = yPixelBase + j
            If py > MaxY Then py = MaxY
            Dim rowInputOffset As Integer = py * Width * 4
            For i As Integer = 0 To 3
                Dim px As Integer = xPixelBase + i
                If px > MaxX Then px = MaxX
                Dim pixelIdx As Integer = rowInputOffset + (px * 4)
                Dim b As Integer = SourceData(pixelIdx)
                Dim g As Integer = SourceData(pixelIdx + 1)
                Dim r As Integer = SourceData(pixelIdx + 2)
                Dim a As Integer = SourceData(pixelIdx + 3)
                If a < 128 Then
                    AlphaMask = AlphaMask Or CUShort(1 << idx)
                End If
                LocalB(idx) = b
                LocalG(idx) = g
                LocalR(idx) = r
                LocalA(idx) = a
                idx += 1
            Next
        Next
        Dim dummyPBits As Integer = 0
        GetEndpointsPCA(0, 0, LocalR, LocalG, LocalB, LocalA, 0, 0, 0, Endpoints, 0, Indices, dummyPBits)
        Dim ep0R As Integer = Endpoints(0) : Dim ep1R As Integer = Endpoints(1)
        Dim ep0G As Integer = Endpoints(2) : Dim ep1G As Integer = Endpoints(3)
        Dim ep0B As Integer = Endpoints(4) : Dim ep1B As Integer = Endpoints(5)
        Dim rangeR As Integer = ep1R - ep0R
        Dim rangeG As Integer = ep1G - ep0G
        Dim rangeB As Integer = ep1B - ep0B
        Dim insetR As Integer = (rangeR + 8) >> 4
        Dim insetG As Integer = (rangeG + 8) >> 4
        Dim insetB As Integer = (rangeB + 8) >> 4
        ep0R = Math.Min(255, Math.Max(0, ep0R + insetR))
        ep1R = Math.Min(255, Math.Max(0, ep1R - insetR))
        ep0G = Math.Min(255, Math.Max(0, ep0G + insetG))
        ep1G = Math.Min(255, Math.Max(0, ep1G - insetG))
        ep0B = Math.Min(255, Math.Max(0, ep0B + insetB))
        ep1B = Math.Min(255, Math.Max(0, ep1B - insetB))
        Dim Col0 As UShort = CUShort(((ep0R And &HF8) << 8) Or ((ep0G And &HFC) << 3) Or (ep0B >> 3))
        Dim Col1 As UShort = CUShort(((ep1R And &HF8) << 8) Or ((ep1G And &HFC) << 3) Or (ep1B >> 3))
        If Col0 < Col1 Then
            Dim temp As UShort = Col0 : Col0 = Col1 : Col1 = temp
        End If
        Dim R0 As Integer = (Col0 >> 8) And &HF8 : R0 = R0 Or (R0 >> 5) : Dim R1 As Integer = (Col1 >> 8) And &HF8 : R1 = R1 Or (R1 >> 5)
        Dim G0 As Integer = (Col0 >> 3) And &HFC : G0 = G0 Or (G0 >> 6) : Dim G1 As Integer = (Col1 >> 3) And &HFC : G1 = G1 Or (G1 >> 6)
        Dim B0 As Integer = (Col0 << 3) And &HF8 : B0 = B0 Or (B0 >> 5) : Dim B1 As Integer = (Col1 << 3) And &HF8 : B1 = B1 Or (B1 >> 5)
        Dim R2_4 As Integer = (2 * R0 + R1 + 1) \ 3 : Dim G2_4 As Integer = (2 * G0 + G1 + 1) \ 3 : Dim B2_4 As Integer = (2 * B0 + B1 + 1) \ 3
        Dim R3_4 As Integer = (R0 + 2 * R1 + 1) \ 3 : Dim G3_4 As Integer = (G0 + 2 * G1 + 1) \ 3 : Dim B3_4 As Integer = (B0 + 2 * B1 + 1) \ 3
        Dim R2_3 As Integer = (R0 + R1) \ 2 : Dim G2_3 As Integer = (G0 + G1) \ 2 : Dim B2_3 As Integer = (B0 + B1) \ 2
        Dim ColorTable4c As UInteger = 0
        Dim ColorTable3c As UInteger = 0
        Dim TotalErr4c As Integer = 0
        Dim TotalErr3c As Integer = 0
        Dim shift As Integer = 0
        For i As Integer = 0 To 15
            Dim PixR As Integer = LocalR(i)
            Dim PixG As Integer = LocalG(i)
            Dim PixB As Integer = LocalB(i)
            Dim dR As Integer = PixR - R0 : Dim dG As Integer = PixG - G0 : Dim dB As Integer = PixB - B0
            Dim err0 As Integer = (dR * dR * 3) + (dG * dG * 4) + (dB * dB * 2)
            dR = PixR - R1 : dG = PixG - G1 : dB = PixB - B1
            Dim err1 As Integer = (dR * dR * 3) + (dG * dG * 4) + (dB * dB * 2)
            Dim minErr4 As Integer = err0
            Dim Index4 As UInteger = 0
            If err1 < minErr4 Then minErr4 = err1 : Index4 = 1
            dR = PixR - R2_4 : dG = PixG - G2_4 : dB = PixB - B2_4
            Dim err2_4 As Integer = (dR * dR * 3) + (dG * dG * 4) + (dB * dB * 2)
            If err2_4 < minErr4 Then minErr4 = err2_4 : Index4 = 2
            dR = PixR - R3_4 : dG = PixG - G3_4 : dB = PixB - B3_4
            Dim err3_4 As Integer = (dR * dR * 3) + (dG * dG * 4) + (dB * dB * 2)
            If err3_4 < minErr4 Then minErr4 = err3_4 : Index4 = 3
            TotalErr4c += minErr4
            ColorTable4c = ColorTable4c Or (Index4 << shift)
            Dim minErr3 As Integer = err0
            Dim Index3 As UInteger = 1
            If err1 < minErr3 Then minErr3 = err1 : Index3 = 0
            dR = PixR - R2_3 : dG = PixG - G2_3 : dB = PixB - B2_3
            Dim err2_3 As Integer = (dR * dR * 3) + (dG * dG * 4) + (dB * dB * 2)
            If err2_3 < minErr3 Then minErr3 = err2_3 : Index3 = 2
            TotalErr3c += minErr3
            ColorTable3c = ColorTable3c Or (Index3 << shift)
            shift += 2
        Next
        Dim FinalCol0 As UShort
        Dim FinalCol1 As UShort
        Dim FinalColorTable As UInteger
        If TotalErr3c < TotalErr4c Then
            FinalCol0 = Col1
            FinalCol1 = Col0
            FinalColorTable = ColorTable3c
        Else
            FinalCol0 = Col0
            FinalCol1 = Col1
            FinalColorTable = ColorTable4c
        End If
        Result(OutputOffset) = CByte(FinalCol0 And &HFF)
        Result(OutputOffset + 1) = CByte(FinalCol0 >> 8)
        Result(OutputOffset + 2) = CByte(FinalCol1 And &HFF)
        Result(OutputOffset + 3) = CByte(FinalCol1 >> 8)
        Result(OutputOffset + 4) = CByte(FinalColorTable And &HFF)
        Result(OutputOffset + 5) = CByte((FinalColorTable >> 8) And &HFF)
        Result(OutputOffset + 6) = CByte((FinalColorTable >> 16) And &HFF)
        Result(OutputOffset + 7) = CByte((FinalColorTable >> 24) And &HFF)
    End Sub

#Region "BC7 Modes"

    Private Sub EncodeMode7(PartitionID As Integer, Endpoints() As Integer, Indices() As Integer, Result As Byte(), OutputOffset As Integer, PBits As Integer)
        Dim subMask = PartitionTable2(PartitionID)
        Dim anchor0 As Integer = 0
        Dim anchor1 As Integer = AnchorIndexTable2(PartitionID)
        Dim t As Integer
        Dim p0 As ULong = CULng((PBits >> 3) And 1)
        Dim p1 As ULong = CULng((PBits >> 2) And 1)
        Dim p2 As ULong = CULng((PBits >> 1) And 1)
        Dim p3 As ULong = CULng(PBits And 1)
        If Indices(anchor0) >= 2 Then
            t = Endpoints(0) : Endpoints(0) = Endpoints(1) : Endpoints(1) = t
            t = Endpoints(2) : Endpoints(2) = Endpoints(3) : Endpoints(3) = t
            t = Endpoints(4) : Endpoints(4) = Endpoints(5) : Endpoints(5) = t
            t = Endpoints(6) : Endpoints(6) = Endpoints(7) : Endpoints(7) = t
            Dim pTemp As ULong = p0 : p0 = p1 : p1 = pTemp
            For i = 0 To 15
                If ((subMask >> i) And 1) = 0 Then Indices(i) = 3 - Indices(i)
            Next
        End If
        If Indices(anchor1) >= 2 Then
            t = Endpoints(8) : Endpoints(8) = Endpoints(9) : Endpoints(9) = t
            t = Endpoints(10) : Endpoints(10) = Endpoints(11) : Endpoints(11) = t
            t = Endpoints(12) : Endpoints(12) = Endpoints(13) : Endpoints(13) = t
            t = Endpoints(14) : Endpoints(14) = Endpoints(15) : Endpoints(15) = t
            Dim pTemp As ULong = p2 : p2 = p3 : p3 = pTemp
            For i = 0 To 15
                If ((subMask >> i) And 1) = 1 Then Indices(i) = 3 - Indices(i)
            Next
        End If
        Dim LowBytes As ULong = 128UL
        LowBytes = LowBytes Or (CULng(PartitionID) << 8)
        LowBytes = LowBytes Or (CULng(Endpoints(0)) << 14)
        LowBytes = LowBytes Or (CULng(Endpoints(1)) << 19)
        LowBytes = LowBytes Or (CULng(Endpoints(8)) << 24)
        LowBytes = LowBytes Or (CULng(Endpoints(9)) << 29)
        LowBytes = LowBytes Or (CULng(Endpoints(2)) << 34)
        LowBytes = LowBytes Or (CULng(Endpoints(3)) << 39)
        LowBytes = LowBytes Or (CULng(Endpoints(10)) << 44)
        LowBytes = LowBytes Or (CULng(Endpoints(11)) << 49)
        LowBytes = LowBytes Or (CULng(Endpoints(4)) << 54)
        LowBytes = LowBytes Or (CULng(Endpoints(5)) << 59)
        Dim HighBytes As ULong = CULng(Endpoints(12))
        HighBytes = HighBytes Or (CULng(Endpoints(13)) << 5)
        HighBytes = HighBytes Or (CULng(Endpoints(6)) << 10)
        HighBytes = HighBytes Or (CULng(Endpoints(7)) << 15)
        HighBytes = HighBytes Or (CULng(Endpoints(14)) << 20)
        HighBytes = HighBytes Or (CULng(Endpoints(15)) << 25)
        HighBytes = HighBytes Or (p0 << 30)
        HighBytes = HighBytes Or (p1 << 31)
        HighBytes = HighBytes Or (p2 << 32)
        HighBytes = HighBytes Or (p3 << 33)
        Dim bitOffset As Integer = 34
        For i = 0 To 15
            Dim bits As Integer = If(i = anchor0 OrElse i = anchor1, 1, 2)
            HighBytes = HighBytes Or (CULng(Indices(i)) << bitOffset)
            bitOffset += bits
        Next
        For i As Integer = 0 To 7
            Result(OutputOffset + i) = CByte((LowBytes >> (i << 3)) And &HFFUL)
            Result(OutputOffset + 8 + i) = CByte((HighBytes >> (i << 3)) And &HFFUL)
        Next
    End Sub

    Private Sub EncodeMode6(Endpoints() As Integer, Indices() As Integer, Result As Byte(), OutputOffset As Integer, PBits As Integer)
        Dim p0 As ULong = CULng((PBits >> 1) And 1)
        Dim p1 As ULong = CULng(PBits And 1)
        If Indices(0) >= 8 Then
            Dim t As Integer
            t = Endpoints(0) : Endpoints(0) = Endpoints(1) : Endpoints(1) = t
            t = Endpoints(2) : Endpoints(2) = Endpoints(3) : Endpoints(3) = t
            t = Endpoints(4) : Endpoints(4) = Endpoints(5) : Endpoints(5) = t
            t = Endpoints(6) : Endpoints(6) = Endpoints(7) : Endpoints(7) = t
            Dim pTemp As ULong = p0 : p0 = p1 : p1 = pTemp
            For i As Integer = 0 To 15
                Indices(i) = 15 - Indices(i)
            Next
        End If
        Dim LowBytes As ULong = &H40UL
        LowBytes = LowBytes Or (CULng(Endpoints(0)) << 7)
        LowBytes = LowBytes Or (CULng(Endpoints(1)) << 14)
        LowBytes = LowBytes Or (CULng(Endpoints(2)) << 21)
        LowBytes = LowBytes Or (CULng(Endpoints(3)) << 28)
        LowBytes = LowBytes Or (CULng(Endpoints(4)) << 35)
        LowBytes = LowBytes Or (CULng(Endpoints(5)) << 42)
        LowBytes = LowBytes Or (CULng(Endpoints(6)) << 49)
        LowBytes = LowBytes Or (CULng(Endpoints(7)) << 56)
        LowBytes = LowBytes Or (p0 << 63)
        Dim HighBytes As ULong = p1
        HighBytes = HighBytes Or ((CULng(Indices(0)) And 7UL) << 1)
        For i As Integer = 1 To 15
            HighBytes = HighBytes Or ((CULng(Indices(i)) And 15UL) << (i * 4))
        Next
        For i As Integer = 0 To 7
            Result(OutputOffset + i) = CByte((LowBytes >> (i << 3)) And &HFFUL)
            Result(OutputOffset + 8 + i) = CByte((HighBytes >> (i << 3)) And &HFFUL)
        Next
    End Sub

    Private Sub EncodeMode5(ColorEndpoints() As Integer, AlphaEndpoints() As Integer, ColorIndices() As Integer, AlphaIndices() As Integer, Rotation As Integer, Result As Byte(), OutputOffset As Integer)
        If ColorIndices(0) >= 2 Then
            Dim t As Integer
            t = ColorEndpoints(0) : ColorEndpoints(0) = ColorEndpoints(1) : ColorEndpoints(1) = t
            t = ColorEndpoints(2) : ColorEndpoints(2) = ColorEndpoints(3) : ColorEndpoints(3) = t
            t = ColorEndpoints(4) : ColorEndpoints(4) = ColorEndpoints(5) : ColorEndpoints(5) = t
            For i As Integer = 0 To 15
                ColorIndices(i) = 3 - ColorIndices(i)
            Next
        End If
        If AlphaIndices(0) >= 2 Then
            Dim t As Integer
            t = AlphaEndpoints(0) : AlphaEndpoints(0) = AlphaEndpoints(1) : AlphaEndpoints(1) = t
            For i As Integer = 0 To 15
                AlphaIndices(i) = 3 - AlphaIndices(i)
            Next
        End If
        Dim LowBytes As ULong = &H20UL
        LowBytes = LowBytes Or (CULng(Rotation And 3) << 6)
        LowBytes = LowBytes Or (CULng(ColorEndpoints(0) And &H7F) << 8)
        LowBytes = LowBytes Or (CULng(ColorEndpoints(1) And &H7F) << 15)
        LowBytes = LowBytes Or (CULng(ColorEndpoints(2) And &H7F) << 22)
        LowBytes = LowBytes Or (CULng(ColorEndpoints(3) And &H7F) << 29)
        LowBytes = LowBytes Or (CULng(ColorEndpoints(4) And &H7F) << 36)
        LowBytes = LowBytes Or (CULng(ColorEndpoints(5) And &H7F) << 43)
        LowBytes = LowBytes Or (CULng(AlphaEndpoints(0) And &HFF) << 50)
        Dim A1 As ULong = CULng(AlphaEndpoints(1) And &HFF)
        LowBytes = LowBytes Or ((A1 And &H3FUL) << 58)
        Dim HighBytes As ULong = (A1 >> 6)
        HighBytes = HighBytes Or ((CULng(ColorIndices(0)) And 1UL) << 2)
        Dim shift As Integer = 3
        For i As Integer = 1 To 15
            HighBytes = HighBytes Or ((CULng(ColorIndices(i)) And 3UL) << shift)
            shift += 2
        Next
        HighBytes = HighBytes Or ((CULng(AlphaIndices(0)) And 1UL) << shift)
        shift += 1
        For i As Integer = 1 To 15
            HighBytes = HighBytes Or ((CULng(AlphaIndices(i)) And 3UL) << shift)
            shift += 2
        Next
        For i As Integer = 0 To 7
            Result(OutputOffset + i) = CByte((LowBytes >> (i << 3)) And &HFFUL)
            Result(OutputOffset + 8 + i) = CByte((HighBytes >> (i << 3)) And &HFFUL)
        Next
    End Sub

    Private Sub EncodeMode4(ColorEndpoints() As Integer, AlphaEndpoints() As Integer, ColorIndices() As Integer, AlphaIndices() As Integer, Rotation As Integer, Result As Byte(), OutputOffset As Integer)
        If ColorIndices(0) >= 2 Then
            Dim t As Integer
            t = ColorEndpoints(0) : ColorEndpoints(0) = ColorEndpoints(1) : ColorEndpoints(1) = t
            t = ColorEndpoints(2) : ColorEndpoints(2) = ColorEndpoints(3) : ColorEndpoints(3) = t
            t = ColorEndpoints(4) : ColorEndpoints(4) = ColorEndpoints(5) : ColorEndpoints(5) = t
            For i As Integer = 0 To 15
                ColorIndices(i) = 3 - ColorIndices(i)
            Next
        End If
        If AlphaIndices(0) >= 4 Then
            Dim t As Integer
            t = AlphaEndpoints(0) : AlphaEndpoints(0) = AlphaEndpoints(1) : AlphaEndpoints(1) = t
            For i As Integer = 0 To 15
                AlphaIndices(i) = 7 - AlphaIndices(i)
            Next
        End If
        Dim LowBytes As ULong = &H10UL
        LowBytes = LowBytes Or (CULng(Rotation And 3) << 5)
        LowBytes = LowBytes Or (CULng(ColorEndpoints(0) And &H1F) << 8)
        LowBytes = LowBytes Or (CULng(ColorEndpoints(1) And &H1F) << 13)
        LowBytes = LowBytes Or (CULng(ColorEndpoints(2) And &H1F) << 18)
        LowBytes = LowBytes Or (CULng(ColorEndpoints(3) And &H1F) << 23)
        LowBytes = LowBytes Or (CULng(ColorEndpoints(4) And &H1F) << 28)
        LowBytes = LowBytes Or (CULng(ColorEndpoints(5) And &H1F) << 33)
        LowBytes = LowBytes Or (CULng(AlphaEndpoints(0) And &H3F) << 38)
        LowBytes = LowBytes Or (CULng(AlphaEndpoints(1) And &H3F) << 44)
        LowBytes = LowBytes Or ((CULng(ColorIndices(0)) And 1UL) << 50)
        LowBytes = LowBytes Or ((CULng(ColorIndices(1)) And 3UL) << 51)
        LowBytes = LowBytes Or ((CULng(ColorIndices(2)) And 3UL) << 53)
        LowBytes = LowBytes Or ((CULng(ColorIndices(3)) And 3UL) << 55)
        LowBytes = LowBytes Or ((CULng(ColorIndices(4)) And 3UL) << 57)
        LowBytes = LowBytes Or ((CULng(ColorIndices(5)) And 3UL) << 59)
        LowBytes = LowBytes Or ((CULng(ColorIndices(6)) And 3UL) << 61)
        LowBytes = LowBytes Or ((CULng(ColorIndices(7)) And 1UL) << 63)
        Dim HighBytes As ULong = ((CULng(ColorIndices(7)) And 2UL) >> 1)
        HighBytes = HighBytes Or ((CULng(ColorIndices(8)) And 3UL) << 1)
        HighBytes = HighBytes Or ((CULng(ColorIndices(9)) And 3UL) << 3)
        HighBytes = HighBytes Or ((CULng(ColorIndices(10)) And 3UL) << 5)
        HighBytes = HighBytes Or ((CULng(ColorIndices(11)) And 3UL) << 7)
        HighBytes = HighBytes Or ((CULng(ColorIndices(12)) And 3UL) << 9)
        HighBytes = HighBytes Or ((CULng(ColorIndices(13)) And 3UL) << 11)
        HighBytes = HighBytes Or ((CULng(ColorIndices(14)) And 3UL) << 13)
        HighBytes = HighBytes Or ((CULng(ColorIndices(15)) And 3UL) << 15)
        HighBytes = HighBytes Or ((CULng(AlphaIndices(0)) And 3UL) << 17)
        For i = 1 To 15
            HighBytes = HighBytes Or ((CULng(AlphaIndices(i)) And 7UL) << (16 + (i * 3)))
        Next
        For i As Integer = 0 To 7
            Result(OutputOffset + i) = CByte((LowBytes >> (i << 3)) And &HFFUL)
            Result(OutputOffset + 8 + i) = CByte((HighBytes >> (i << 3)) And &HFFUL)
        Next
    End Sub

    Private Sub EncodeMode1(PartitionID As Integer, Endpoints() As Integer, Indices() As Integer, Result As Byte(), OutputOffset As Integer, PBits As Integer)
        Dim subMask = PartitionTable2(PartitionID)
        Dim anchor0 As Integer = 0
        Dim anchor1 As Integer = AnchorIndexTable2(PartitionID)
        Dim t As Integer
        If Indices(anchor0) >= 4 Then
            t = Endpoints(0) : Endpoints(0) = Endpoints(1) : Endpoints(1) = t
            t = Endpoints(2) : Endpoints(2) = Endpoints(3) : Endpoints(3) = t
            t = Endpoints(4) : Endpoints(4) = Endpoints(5) : Endpoints(5) = t
            For i = 0 To 15
                If ((subMask >> i) And 1) = 0 Then Indices(i) = 7 - Indices(i)
            Next
        End If
        If Indices(anchor1) >= 4 Then
            t = Endpoints(8) : Endpoints(8) = Endpoints(9) : Endpoints(9) = t
            t = Endpoints(10) : Endpoints(10) = Endpoints(11) : Endpoints(11) = t
            t = Endpoints(12) : Endpoints(12) = Endpoints(13) : Endpoints(13) = t
            For i = 0 To 15
                If ((subMask >> i) And 1) = 1 Then Indices(i) = 7 - Indices(i)
            Next
        End If
        Dim LowBytes As ULong = 2UL
        LowBytes = LowBytes Or (CULng(PartitionID) << 2)
        LowBytes = LowBytes Or (CULng(Endpoints(0)) << 8)
        LowBytes = LowBytes Or (CULng(Endpoints(1)) << 14)
        LowBytes = LowBytes Or (CULng(Endpoints(8)) << 20)
        LowBytes = LowBytes Or (CULng(Endpoints(9)) << 26)
        LowBytes = LowBytes Or (CULng(Endpoints(2)) << 32)
        LowBytes = LowBytes Or (CULng(Endpoints(3)) << 38)
        LowBytes = LowBytes Or (CULng(Endpoints(10)) << 44)
        LowBytes = LowBytes Or (CULng(Endpoints(11)) << 50)
        LowBytes = LowBytes Or (CULng(Endpoints(4)) << 56)
        LowBytes = LowBytes Or ((CULng(Endpoints(5)) And 3UL) << 62)
        Dim HighBytes As ULong = ((CULng(Endpoints(5)) >> 2) And 15UL)
        HighBytes = HighBytes Or (CULng(Endpoints(12)) << 4)
        HighBytes = HighBytes Or (CULng(Endpoints(13)) << 10)
        HighBytes = HighBytes Or (CULng(PBits And &H8) << 13)
        HighBytes = HighBytes Or (CULng(PBits And &H2) << 16)
        Dim bitOffset As Integer = 18
        For i = 0 To 15
            Dim bits As Integer = If(i = anchor0 OrElse i = anchor1, 2, 3)
            HighBytes = HighBytes Or (CULng(Indices(i)) << bitOffset)
            bitOffset += bits
        Next
        For i As Integer = 0 To 7
            Result(OutputOffset + i) = CByte((LowBytes >> (i << 3)) And &HFFUL)
            Result(OutputOffset + 8 + i) = CByte((HighBytes >> (i << 3)) And &HFFUL)
        Next
    End Sub

    Private Sub GetEndpointsPCA(subMask As Integer, targetSubset As Integer, LocalR() As Integer, LocalG() As Integer, LocalB() As Integer, LocalA() As Integer, endpointShift As Integer, indexMax As Integer, alphaMult As Integer, Endpoints() As Integer, epOffset As Integer, indices() As Integer, ByRef PBits As Integer)
        Dim count As Integer = 0
        Dim sumR As Integer = 0, sumG As Integer = 0, sumB As Integer = 0, sumA As Integer = 0
        Dim sumRR As Integer = 0, sumGG As Integer = 0, sumBB As Integer = 0, sumAA As Integer = 0
        Dim sumRG As Integer = 0, sumRB As Integer = 0, sumRA As Integer = 0
        Dim sumGB As Integer = 0, sumGA As Integer = 0, sumBA As Integer = 0
        For i As Integer = 0 To 15
            If ((subMask >> i) And 1) = targetSubset Then
                Dim r As Integer = LocalR(i)
                Dim g As Integer = LocalG(i)
                Dim b As Integer = LocalB(i)
                Dim a As Integer = LocalA(i) * alphaMult
                sumR += r : sumG += g : sumB += b : sumA += a
                sumRR += r * r : sumGG += g * g : sumBB += b * b : sumAA += a * a
                sumRG += r * g : sumRB += r * b : sumRA += r * a
                sumGB += g * b : sumGA += g * a
                sumBA += b * a
                count += 1
            End If
        Next
        If count = 0 Then Return
        Dim invCount As Single = 1.0F / CSng(count)
        Dim meanR As Single = sumR * invCount
        Dim meanG As Single = sumG * invCount
        Dim meanB As Single = sumB * invCount
        Dim meanA As Single = sumA * invCount
        Dim c00 As Single = sumRR - (sumR * meanR)
        Dim c11 As Single = sumGG - (sumG * meanG)
        Dim c22 As Single = sumBB - (sumB * meanB)
        Dim c33 As Single = sumAA - (sumA * meanA)
        Dim c01 As Single = sumRG - (sumR * meanG)
        Dim c02 As Single = sumRB - (sumR * meanB)
        Dim c03 As Single = sumRA - (sumR * meanA)
        Dim c12 As Single = sumGB - (sumG * meanB)
        Dim c13 As Single = sumGA - (sumG * meanA)
        Dim c23 As Single = sumBA - (sumB * meanA)
        Dim vR As Single = 0.8F, vG As Single = 0.9F, vB As Single = 0.7F, vA As Single = CSng(alphaMult)
        For iter As Integer = 1 To 4
            Dim nvR As Single = c00 * vR + c01 * vG + c02 * vB + c03 * vA
            Dim nvG As Single = c01 * vR + c11 * vG + c12 * vB + c13 * vA
            Dim nvB As Single = c02 * vR + c12 * vG + c22 * vB + c23 * vA
            Dim nvA As Single = c03 * vR + c13 * vG + c23 * vB + c33 * vA
            Dim magSq As Single = nvR * nvR + nvG * nvG + nvB * nvB + nvA * nvA
            If magSq < 0.00001F Then Exit For
            Dim invMag As Single = 1.0F / CSng(Math.Sqrt(magSq))
            vR = nvR * invMag : vG = nvG * invMag : vB = nvB * invMag : vA = nvA * invMag
        Next
        Dim minProj As Single = Single.MaxValue
        Dim maxProj As Single = Single.MinValue
        Dim vA_scaled As Single = vA * alphaMult
        For i As Integer = 0 To 15
            If ((subMask >> i) And 1) = targetSubset Then
                Dim proj As Single = LocalR(i) * vR + LocalG(i) * vG + LocalB(i) * vB + LocalA(i) * vA_scaled
                If proj < minProj Then minProj = proj
                If proj > maxProj Then maxProj = proj
            End If
        Next
        Dim meanProj As Single = meanR * vR + meanG * vG + meanB * vB + meanA * vA
        minProj -= meanProj
        maxProj -= meanProj
        If minProj = maxProj Then minProj -= 1.0F : maxProj += 1.0F
        Dim ep0R As Integer = Math.Min(255, Math.Max(0, CInt(meanR + vR * minProj)))
        Dim ep1R As Integer = Math.Min(255, Math.Max(0, CInt(meanR + vR * maxProj)))
        Dim ep0G As Integer = Math.Min(255, Math.Max(0, CInt(meanG + vG * minProj)))
        Dim ep1G As Integer = Math.Min(255, Math.Max(0, CInt(meanG + vG * maxProj)))
        Dim ep0B As Integer = Math.Min(255, Math.Max(0, CInt(meanB + vB * minProj)))
        Dim ep1B As Integer = Math.Min(255, Math.Max(0, CInt(meanB + vB * maxProj)))
        Dim ep0A As Integer = Math.Min(255, Math.Max(0, CInt(meanA + vA * minProj)))
        Dim ep1A As Integer = Math.Min(255, Math.Max(0, CInt(meanA + vA * maxProj)))
        Dim p0 As Integer = (ep0G >> (endpointShift - 1)) And 1
        Dim p1 As Integer = (ep1G >> (endpointShift - 1)) And 1
        PBits = (PBits << 2) Or (p0 << 1) Or p1
        Endpoints(epOffset + 0) = ep0R >> endpointShift : Endpoints(epOffset + 1) = ep1R >> endpointShift
        Endpoints(epOffset + 2) = ep0G >> endpointShift : Endpoints(epOffset + 3) = ep1G >> endpointShift
        Endpoints(epOffset + 4) = ep0B >> endpointShift : Endpoints(epOffset + 5) = ep1B >> endpointShift
        Endpoints(epOffset + 6) = ep0A >> endpointShift : Endpoints(epOffset + 7) = ep1A >> endpointShift
        If indexMax = 0 Then Return
        Dim dirR As Integer = ep1R - ep0R
        Dim dirG As Integer = ep1G - ep0G
        Dim dirB As Integer = ep1B - ep0B
        Dim dirA As Integer = (ep1A - ep0A) * alphaMult
        Dim den As Integer = (dirR * dirR) + (dirG * dirG) + (dirB * dirB) + (dirA * dirA)
        If den < 1 Then den = 1
        Dim halfDen As Integer = den >> 1
        Dim ep0DotDir As Integer = (ep0R * dirR) + (ep0G * dirG) + (ep0B * dirB) + (ep0A * dirA)
        For i As Integer = 0 To 15
            If ((subMask >> i) And 1) = targetSubset Then
                Dim rawDot As Integer = (LocalR(i) * dirR) + (LocalG(i) * dirG) + (LocalB(i) * dirB) + (LocalA(i) * dirA)
                Dim dot As Integer = rawDot - ep0DotDir
                Dim index As Integer = ((dot * indexMax) + halfDen) \ den
                If index > indexMax Then index = indexMax Else If index < 0 Then index = 0
                indices(i) = index
            End If
        Next
    End Sub

    Private Sub GetEndpoints1D(LocalChannel() As Integer, EndpointBits As Integer, IndexBits As Integer, Endpoints() As Integer, epOffset As Integer, Indices() As Integer, minVal As Integer, maxVal As Integer)
        Dim maxQuant As Integer = (1 << EndpointBits) - 1
        If minVal = maxVal Then
            Dim ep As Integer = (minVal * maxQuant + 127) \ 255
            Endpoints(epOffset + 0) = ep
            Endpoints(epOffset + 1) = ep
            For i As Integer = 0 To 15
                Indices(i) = 0
            Next
            Return
        End If
        If IndexBits = 0 Then
            Endpoints(epOffset + 0) = (minVal * maxQuant + 127) \ 255
            Endpoints(epOffset + 1) = (maxVal * maxQuant + 127) \ 255
            Return
        End If
        Dim valRange As Integer = maxVal - minVal
        Dim inset As Integer = valRange >> 4
        Dim insetMin As Integer = minVal + inset
        Dim insetMax As Integer = maxVal - inset
        Dim ep0 As Integer = (insetMin * maxQuant + 127) \ 255
        Dim ep1 As Integer = (insetMax * maxQuant + 127) \ 255
        Endpoints(epOffset + 0) = ep0
        Endpoints(epOffset + 1) = ep1
        Dim shift As Integer = 8 - EndpointBits
        Dim ep0_8 As Integer = (ep0 << shift) Or (ep0 >> (EndpointBits - shift))
        Dim ep1_8 As Integer = (ep1 << shift) Or (ep1 >> (EndpointBits - shift))
        Dim range As Integer = ep1_8 - ep0_8
        If range = 0 Then range = 1
        Dim indexMax As Integer = (1 << IndexBits) - 1
        For i As Integer = 0 To 15
            Dim dist As Integer = LocalChannel(i) - ep0_8
            Dim idx As Integer = (dist * indexMax + (range >> 1)) \ range
            If idx < 0 Then idx = 0 Else If idx > indexMax Then idx = indexMax
            Indices(i) = idx
        Next
    End Sub

#End Region

    Public Sub Save(FilePath As String)
        If CubeFaces IsNot Nothing Then
            BeginEncodeCube()
        Else
            BeginEncode()
        End If
        File.WriteAllBytes(FilePath, PayloadBytes)
    End Sub

    Public Function ToBytes() As Byte()
        If CubeFaces IsNot Nothing Then
            BeginEncodeCube()
        Else
            BeginEncode()
        End If
        Return PayloadBytes
    End Function

    Private Function CalcMips(Width As Integer, Height As Integer) As Integer
        Dim xMips As Integer = GetDivTwo(Width)
        Dim yMips As Integer = GetDivTwo(Height)
        Return Math.Min(xMips, yMips) + 1
    End Function

    Private Function GetDivTwo(Source As Integer) As Integer
        Dim Count As Integer = 0
        Dim TempSize As Integer = Source
        While TempSize > 1
            TempSize >>= 1
            Count += 1
        End While
        Return Count
    End Function

    Private Function HalveArray(SourceData() As Byte, Width As Integer, Height As Integer) As Byte()
        Dim TempWidth As Integer = Math.Max(1, Width >> 1)
        Dim TempHeight As Integer = Math.Max(1, Height >> 1)
        Dim DestData(TempWidth * TempHeight * 4 - 1) As Byte
        Parallel.For(0, TempHeight, Options, Sub(y)
                                                 Dim destRowOffset As Integer = y * TempWidth * 4
                                                 For x As Integer = 0 To TempWidth - 1
                                                     Dim destPixelOffset As Integer = destRowOffset + (x * 4)
                                                     Dim sumB As Integer = 0, sumG As Integer = 0, sumR As Integer = 0, sumA As Integer = 0
                                                     Dim weightIdx As Integer = 0
                                                     For sy As Integer = -1 To 2
                                                         Dim srcY As Integer = Math.Max(0, Math.Min((y << 1) + sy, Height - 1))
                                                         Dim srcRowOffset As Integer = srcY * Width * 4
                                                         For sx As Integer = -1 To 2
                                                             Dim srcX As Integer = Math.Max(0, Math.Min((x << 1) + sx, Width - 1))
                                                             Dim srcPixelOffset As Integer = srcRowOffset + (srcX * 4)
                                                             Dim w As Integer = Weight4x4(weightIdx)
                                                             weightIdx += 1
                                                             sumB += SourceData(srcPixelOffset) * w
                                                             sumG += SourceData(srcPixelOffset + 1) * w
                                                             sumR += SourceData(srcPixelOffset + 2) * w
                                                             sumA += SourceData(srcPixelOffset + 3) * w
                                                         Next
                                                     Next
                                                     DestData(destPixelOffset) = CByte(Math.Max(0, Math.Min(255, sumB >> 8)))
                                                     DestData(destPixelOffset + 1) = CByte(Math.Max(0, Math.Min(255, sumG >> 8)))
                                                     DestData(destPixelOffset + 2) = CByte(Math.Max(0, Math.Min(255, sumR >> 8)))
                                                     DestData(destPixelOffset + 3) = CByte(Math.Max(0, Math.Min(255, sumA >> 8)))
                                                 Next
                                             End Sub)
        Return DestData
    End Function

    Private Function OrderBytes(Source As Integer) As Byte()
        Return BitConverter.GetBytes(Source)
    End Function

    Private Function OrderBytes(Source As String) As Byte()
        Dim Bytes(3) As Byte
        If Not String.IsNullOrEmpty(Source) Then
            Dim Temp As Byte() = System.Text.Encoding.ASCII.GetBytes(Source)
            Array.Copy(Temp, Bytes, Math.Min(Temp.Length, 4))
        End If
        Return Bytes
    End Function

    Protected Overridable Sub Dispose(Disposing As Boolean)
        If Not Disposed Then
            CubeFaces = Nothing
            HeaderBytes = Nothing
            PayloadBytes = Nothing
        End If
        Disposed = True
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose(True)
        GC.SuppressFinalize(Me)
    End Sub

End Class
