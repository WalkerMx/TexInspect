' DDS Decoder Class by WalkerMx
' Based on the documentation found here:
' http://doc.51windows.net/directx9_sdk/graphics/reference/DDSFileReference/ddsfileformat.htm
' https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dx-graphics-dds

Imports System.IO

Public Class DDS_Decoder
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

    Public RedBitMask As UInteger
    Public GreenBitMask As UInteger
    Public BlueBitMask As UInteger
    Public AlphaBitMask As UInteger

    Public Caps1 As DDS_Caps1
    Public Caps2 As DDS_Caps2

    Public DXGIFormat As DXGI_Format
    Public ResourceDimension As DX10_ResourceDimension
    Public MiscFlag As DX10_MiscFlags
    Public ArraySize As Integer
    Public MiscFlags2 As DX10_MiscFlags2

    Public IsCubeMap As Boolean
    Public IsNormalMap As Boolean
    Public ExtendedHeader As Boolean

    Private CubeFaces(5)() As Byte
    Private OutputPixelFormat As PixelFormat = PixelFormats.Bgra32

    Private FilePath As String
    Private SourceBytes As Byte()
    Private EncodedBytes As Byte()
    Private DecodedBytes As Byte()

    <ThreadStatic> Private Shared BufferA As Integer()
    <ThreadStatic> Private Shared BufferB As Integer()
    <ThreadStatic> Private Shared BufferC As Integer()
    <ThreadStatic> Private Shared BufferD As Integer()
    <ThreadStatic> Private Shared BufferE As Integer()
    <ThreadStatic> Private Shared BufferF As Integer()
    <ThreadStatic> Private Shared BufferG As Integer()

    Public Sub New(Source As String)
        FilePath = Source
        If FilePath.ToLower.Contains("_n.dds") Then IsNormalMap = True
        Using FS As New FileStream(Source, FileMode.Open, FileAccess.Read)
            ReadHeader(FS)
        End Using
    End Sub

    Public Sub New(Source As Byte())
        EncodedBytes = Source
        Using FS As New MemoryStream(EncodedBytes)
            ReadHeader(FS)
        End Using
    End Sub

    Private Sub ReadHeader(Source As Stream)
        Using Reader As New BinaryReader(Source)

            Signature = New String(Reader.ReadChars(4))             ' dwMagic
            HeaderSize = Reader.ReadInt32()                         ' dwSize
            SurfaceFlags = Reader.ReadInt32()                       ' dwFlags
            Height = Reader.ReadInt32()                             ' dwHeight
            Width = Reader.ReadInt32()                              ' dwWidth
            PitchLinearSize = Reader.ReadInt32()                    ' dwPitchOrLinearSize
            Depth = Reader.ReadInt32()                              ' dwDepth
            MipMapCount = Reader.ReadInt32()                        ' dwMipMapCount

            Reader.BaseStream.Seek(44, SeekOrigin.Current)          ' dwReserved1 x11

            SubHeaderSize = Reader.ReadInt32()                      ' DDPIXELFORMAT dwSize
            PixelFlags = Reader.ReadInt32()                         ' DDPIXELFORMAT dwFlags
            FourCC = New String(Reader.ReadChars(4))                ' DDPIXELFORMAT dwFourCC
            RGBBitCount = Reader.ReadInt32()                        ' DDPIXELFORMAT dwRGBBitCount
            RedBitMask = Reader.ReadUInt32()                        ' DDPIXELFORMAT dwRBitMask
            GreenBitMask = Reader.ReadUInt32()                      ' DDPIXELFORMAT dwGBitMask
            BlueBitMask = Reader.ReadUInt32()                       ' DDPIXELFORMAT dwBBitMask
            AlphaBitMask = Reader.ReadUInt32()                      ' DDPIXELFORMAT dwABitMask

            Caps1 = Reader.ReadInt32()                              ' dwCaps
            Caps2 = Reader.ReadInt32()                              ' dwCaps2

            Reader.BaseStream.Seek(12, SeekOrigin.Current)          ' dwCaps3, dwCaps4, dwReserved2

            If FourCC = "DX10" Then
                ExtendedHeader = True
                DXGIFormat = Reader.ReadInt32()                     ' dxgiFormat
                ResourceDimension = Reader.ReadInt32()              ' resourceDimension
                MiscFlag = Reader.ReadInt32()                       ' miscFlag
                ArraySize = Reader.ReadInt32()                      ' arraySize
                MiscFlags2 = Reader.ReadInt32()                     ' miscFlags2
                Select Case DXGIFormat
                    Case DXGI_Format.DXGI_FORMAT_B8G8R8A8_UNORM : SetMask(2, 1, 0, 3) : RGBBitCount = 32
                    Case DXGI_Format.DXGI_FORMAT_B8G8R8X8_UNORM : SetMask(2, 1, 0) : RGBBitCount = 32
                End Select
            End If

            If ExtendedHeader Then
                If (MiscFlag And &H4) = &H4 Then IsCubeMap = True   ' D3D10_RESOURCE_MISC_TEXTURECUBE
            Else
                If (Caps2 And &H200) = &H200 Then IsCubeMap = True  ' DDSCAPS2_CUBEMAP = &H200
            End If

        End Using
    End Sub

    Public Sub BeginDecode(Optional MipMapOffset As Long = -1)
        Dim Offset As Long = If(MipMapOffset <> -1, MipMapOffset, If(ExtendedHeader, 148, 128))
        Dim AlphaMode As Integer = 0
        Dim CompressionMode As Integer = 0
        Dim BytesToRead As Integer = 0
        Dim BaseBytes As Integer = Math.Max(1, (Me.Width + 3) \ 4) * Math.Max(1, (Me.Height + 3) \ 4)
        If ExtendedHeader Then
            Select Case DXGIFormat
                Case &H46, &H47, &H48 ' BC1 Typeless, UNORM, SRGB
                    CompressionMode = If(MiscFlags2 = DX10_MiscFlags2.DDS_ALPHA_MODE_OPAQUE, 0, 1)
                    BytesToRead = BaseBytes * 8
                Case &H49, &H4A, &H4B ' BC2 Typeless, UNORM, SRGB
                    CompressionMode = 2
                    BytesToRead = BaseBytes * 16
                Case &H4C, &H4D, &H4E ' BC3 Typeless, UNORM, SRGB
                    CompressionMode = If(IsNormalMap, 30, 3)
                    BytesToRead = BaseBytes * 16
                Case &H4F, &H50 ' BC4 Typeless, UNORM
                    CompressionMode = 4
                    BytesToRead = BaseBytes * 8
                Case &H52, &H53 ' BC5 Typeless, UNORM
                    CompressionMode = 5
                    BytesToRead = BaseBytes * 16
                Case &H61, &H62, &H63 ' BC7 Typeless, UNORM, SRGB
                    CompressionMode = 7
                    BytesToRead = BaseBytes * 16
                Case &H57, &H5A, &H5B ' B8G8R8A8 Typeless, UNORM, SRGB
                    CompressionMode = -1
                    AlphaMode = 2
                    BytesToRead = Me.Width * Me.Height * 4
                Case &H58, &H5C, &H5D ' B8G8R8X8 Typeless, UNORM, SRGB
                    CompressionMode = -1
                    AlphaMode = 0
                    BytesToRead = Me.Width * Me.Height * 4
                Case Else
                    Throw New Exception("Unsupported DXGI Format: " & DXGIFormat.ToString.Replace("DXGI_FORMAT_", ""))
            End Select
            If MiscFlags2 = DX10_MiscFlags2.DDS_ALPHA_MODE_PREMULTIPLIED Then
                OutputPixelFormat = PixelFormats.Pbgra32
            End If
        Else
            If (PixelFlags And DDS_PixelFlags.DDPF_FOURCC) = DDS_PixelFlags.DDPF_FOURCC Then
                Select Case FourCC
                    Case "DXT1"
                        CompressionMode = If((PixelFlags And DDS_PixelFlags.DDPF_ALPHAPIXELS) = DDS_PixelFlags.DDPF_ALPHAPIXELS, 1, 0)
                        BytesToRead = BaseBytes * 8
                    Case "DXT2"
                        CompressionMode = 2
                        BytesToRead = BaseBytes * 16
                        OutputPixelFormat = PixelFormats.Pbgra32
                    Case "DXT3"
                        CompressionMode = 2
                        BytesToRead = BaseBytes * 16
                    Case "DXT4"
                        CompressionMode = 3
                        BytesToRead = BaseBytes * 16
                        OutputPixelFormat = PixelFormats.Pbgra32
                    Case "DXT5"
                        CompressionMode = 3
                        BytesToRead = BaseBytes * 16
                    Case "ATI1", "BC4U"
                        CompressionMode = 4
                        BytesToRead = BaseBytes * 8
                    Case "ATI2", "BC5U"
                        CompressionMode = 5
                        BytesToRead = BaseBytes * 16
                    Case Else
                        Throw New Exception("Unsupported DXT Format: " & FourCC)
                End Select
            ElseIf (PixelFlags And DDS_PixelFlags.DDPF_RGB) = DDS_PixelFlags.DDPF_RGB Then
                CompressionMode = -1
                BytesToRead = Me.Width * Me.Height * (RGBBitCount \ 8)
                AlphaMode = If((PixelFlags And DDS_PixelFlags.DDPF_ALPHAPIXELS) = DDS_PixelFlags.DDPF_ALPHAPIXELS, 2, 0)
            Else
                Throw New Exception("Unknown Format Error!")
            End If
        End If
        If BytesToRead > 0 Then
            If EncodedBytes IsNot Nothing AndAlso EncodedBytes.Length > 0 Then
                Using ms As New MemoryStream(EncodedBytes)
                    SourceBytes = GetFileBytes(ms, Offset, BytesToRead)
                End Using
            Else
                SourceBytes = GetFileBytes(FilePath, Offset, BytesToRead)
            End If
            If CompressionMode = -1 Then
                DecodeUncompressed(AlphaMode)
            Else
                DecodeCompressed(CompressionMode)
            End If
        End If
        SourceBytes = Nothing
    End Sub

    Private Sub DecodeCubeMap()
        If Not IsCubeMap Then Throw New Exception("File is not a CubeMap.")
        Dim HeaderSize As Long = If(ExtendedHeader, 148, 128)
        Dim TotalFileBytes As Long = New FileInfo(FilePath).Length
        Dim FaceChainByteSize As Long = (TotalFileBytes - HeaderSize) \ 6
        For CubeFace As Integer = 0 To 5
            Dim Offset As Long = HeaderSize + (CubeFace * FaceChainByteSize)
            BeginDecode(Offset)
            CubeFaces(CubeFace) = DecodedBytes.Clone()
        Next
    End Sub

    Private Sub DecodeUncompressed(AlphaMode As Integer)
        Dim bytesPerPixel As Integer = RGBBitCount \ 8
        Dim RowPitch As Integer = 0
        If (SurfaceFlags And DDS_SurfaceFlags.DDSD_PITCH) = DDS_SurfaceFlags.DDSD_PITCH Then
            RowPitch = PitchLinearSize
        Else
            RowPitch = (Width * bytesPerPixel + 3) And Not 3
        End If
        Dim rShift As Integer = GetShiftCount(RedBitMask)
        Dim gShift As Integer = GetShiftCount(GreenBitMask)
        Dim bShift As Integer = GetShiftCount(BlueBitMask)
        Dim aShift As Integer = GetShiftCount(AlphaBitMask)
        Dim DecodeAlpha As Boolean = (AlphaMode > 0) AndAlso (AlphaBitMask <> 0)
        DecodedBytes = New Byte(Width * Height * 4 - 1) {}
        Parallel.For(0, Height, Options, Sub(y)
                                             Dim SourceRowStart As Integer = y * RowPitch
                                             Dim DestRowStart As Integer = y * Width * 4
                                             For x As Integer = 0 To Width - 1
                                                 Dim SourceIndex As Integer = SourceRowStart + (x * bytesPerPixel)
                                                 Dim DestIndex As Integer = DestRowStart + (x * 4)
                                                 If SourceIndex + bytesPerPixel > SourceBytes.Length Then Continue For
                                                 Dim pixelValue As UInteger = 0
                                                 If bytesPerPixel = 4 Then
                                                     pixelValue = BitConverter.ToUInt32(SourceBytes, SourceIndex)
                                                 ElseIf bytesPerPixel = 3 Then
                                                     pixelValue = CUInt(SourceBytes(SourceIndex)) Or (CUInt(SourceBytes(SourceIndex + 1)) << 8) Or (CUInt(SourceBytes(SourceIndex + 2)) << 16)
                                                 ElseIf bytesPerPixel = 2 Then
                                                     pixelValue = BitConverter.ToUInt16(SourceBytes, SourceIndex)
                                                 End If
                                                 DecodedBytes(DestIndex) = CByte((pixelValue And CUInt(BlueBitMask)) >> bShift)
                                                 DecodedBytes(DestIndex + 1) = CByte((pixelValue And CUInt(GreenBitMask)) >> gShift)
                                                 DecodedBytes(DestIndex + 2) = CByte((pixelValue And CUInt(RedBitMask)) >> rShift)
                                                 If DecodeAlpha Then
                                                     DecodedBytes(DestIndex + 3) = CByte((pixelValue And CUInt(AlphaBitMask)) >> aShift)
                                                 Else
                                                     DecodedBytes(DestIndex + 3) = 255
                                                 End If
                                             Next
                                         End Sub)
    End Sub

    Private Sub DecodeCompressed(CompressionMode As Integer)
        Dim BlockWidth As Integer = (Width + 3) \ 4
        Dim BlockHeight As Integer = (Height + 3) \ 4
        Dim BytesPerBlock As Integer = If(CompressionMode = 0 OrElse CompressionMode = 1 OrElse CompressionMode = 4, 8, 16)
        DecodedBytes = New Byte(Width * Height * 4 - 1) {}
        Parallel.For(0, BlockHeight, Options, Sub(yBlock)
                                                  Dim yPixelBase As Integer = yBlock * 4
                                                  If BufferA Is Nothing Then
                                                      BufferA = New Integer(15) {} : BufferB = New Integer(15) {}
                                                      BufferC = New Integer(15) {} : BufferD = New Integer(15) {}
                                                      BufferE = New Integer(15) {} : BufferF = New Integer(15) {}
                                                      BufferG = New Integer(15) {}
                                                  End If
                                                  For xBlock As Integer = 0 To BlockWidth - 1
                                                      Dim xPixelBase As Integer = xBlock * 4
                                                      Dim SourceIndex As Integer = (yBlock * BlockWidth + xBlock) * BytesPerBlock
                                                      Select Case CompressionMode
                                                          Case 0, 1 ' BC1 / BC1a / DXT1 / DXT1a
                                                              DecodeBlockBC1(SourceIndex, xPixelBase, yPixelBase, CompressionMode, BufferA, BufferB)
                                                          Case 2 ' BC2 / DXT2-3
                                                              DecodeBlockBC1(SourceIndex + 8, xPixelBase, yPixelBase, CompressionMode, BufferA, BufferB)
                                                              DecodeBlockBC2(SourceIndex, xPixelBase, yPixelBase)
                                                          Case 3 ' BC3 /DXT4-5
                                                              DecodeBlockBC1(SourceIndex + 8, xPixelBase, yPixelBase, CompressionMode, BufferA, BufferB)
                                                              DecodeBlockBC3(SourceIndex, xPixelBase, yPixelBase, 3, BufferA)
                                                          Case 4 ' BC4 / ATI1
                                                              DecodeBlockBC3(SourceIndex, xPixelBase, yPixelBase, 2, BufferA)
                                                              DecodeBlockBC4(xPixelBase, yPixelBase)
                                                          Case 5 ' BC5 / ATI2
                                                              DecodeBlockBC3(SourceIndex, xPixelBase, yPixelBase, 2, BufferB)
                                                              DecodeBlockBC3(SourceIndex + 8, xPixelBase, yPixelBase, 1, BufferA)
                                                              DecodeBlockBC5(xPixelBase, yPixelBase)
                                                          Case 7 ' BC7
                                                              DecodeBlockBC7(SourceIndex, xPixelBase, yPixelBase, BufferA, BufferB, BufferC, BufferD, BufferE, BufferF, BufferG)
                                                          Case 30 ' DXT5n
                                                              DecodeBlockBC1(SourceIndex + 8, xPixelBase, yPixelBase, CompressionMode, BufferA, BufferB)
                                                              DecodeBlockBC3(SourceIndex, xPixelBase, yPixelBase, 3, BufferA)
                                                              DecodeBlockBC3n(xPixelBase, yPixelBase)
                                                      End Select
                                                  Next
                                              End Sub)
    End Sub

    Private Sub DecodeBlockBC7(Offset As Integer, xPixelBase As Integer, yPixelBase As Integer, LocalA() As Integer, LocalB() As Integer, LocalC() As Integer, LocalD() As Integer, LocalE() As Integer, LocalF() As Integer, LocalG() As Integer)
        Dim Mode As Integer = ModeLUT(SourceBytes(Offset))
        Dim BitPos As Integer = Mode + 1
        Select Case Mode
            Case 0 : DecodeMode0(Offset, BitPos, xPixelBase, yPixelBase, LocalA, LocalB, LocalC, LocalD, LocalE, LocalF)
            Case 1 : DecodeMode1(Offset, BitPos, xPixelBase, yPixelBase, LocalA, LocalB, LocalC, LocalD, LocalE, LocalF)
            Case 2 : DecodeMode2(Offset, BitPos, xPixelBase, yPixelBase, LocalA, LocalB, LocalC, LocalD, LocalE)
            Case 3 : DecodeMode3(Offset, BitPos, xPixelBase, yPixelBase, LocalA, LocalB, LocalC, LocalD, LocalE, LocalF)
            Case 4 : DecodeMode4(Offset, BitPos, xPixelBase, yPixelBase, LocalA, LocalB, LocalC, LocalD)
            Case 5 : DecodeMode5(Offset, BitPos, xPixelBase, yPixelBase, LocalA, LocalB)
            Case 6 : DecodeMode6(Offset, BitPos, xPixelBase, yPixelBase)
            Case 7 : DecodeMode7(Offset, BitPos, xPixelBase, yPixelBase, LocalA, LocalB, LocalC, LocalD, LocalE, LocalF, LocalG)
            Case Else
                DecodeModeDiagnostic(Offset, xPixelBase, yPixelBase)
        End Select
    End Sub

    Private Sub DecodeBlockBC5(xPixelBase As Integer, yPixelBase As Integer)
        For i As Integer = 0 To 15
            Dim pX As Integer = xPixelBase + (i And 3)
            Dim pY As Integer = yPixelBase + (i >> 2)
            If pX < Width AndAlso pY < Height Then
                Dim destIdx As Integer = (pY * Width + pX) * 4
                Dim RVal As Integer = DecodedBytes(destIdx + 2)
                Dim GVal As Integer = DecodedBytes(destIdx + 1)
                DecodedBytes(destIdx + 0) = 0
                DecodedBytes(destIdx + 3) = 255
            End If
        Next
    End Sub

    Private Sub DecodeBlockBC4(xPixelBase As Integer, yPixelBase As Integer)
        For i As Integer = 0 To 15
            Dim pX As Integer = xPixelBase + (i And 3)
            Dim pY As Integer = yPixelBase + (i >> 2)
            If pX < Width AndAlso pY < Height Then
                Dim destIdx As Integer = (pY * Width + pX) * 4
                Dim RVal As Byte = DecodedBytes(destIdx + 2)
                DecodedBytes(destIdx) = RVal
                DecodedBytes(destIdx + 1) = RVal
                DecodedBytes(destIdx + 3) = 255
            End If
        Next
    End Sub

    Private Sub DecodeBlockBC3n(xPixelBase As Integer, yPixelBase As Integer)
        For i As Integer = 0 To 15
            Dim pX As Integer = xPixelBase + (i And 3)
            Dim pY As Integer = yPixelBase + (i >> 2)
            If pX < Width AndAlso pY < Height Then
                Dim destIdx As Integer = (pY * Width + pX) * 4
                Dim RVal As Integer = DecodedBytes(destIdx + 3)
                DecodedBytes(destIdx + 2) = RVal
                DecodedBytes(destIdx + 3) = 255
            End If
        Next
    End Sub

    Private Sub DecodeBlockBC3(Offset As Integer, xPixelBase As Integer, yPixelBase As Integer, TargetChannel As Integer, aPal() As Integer)
        Dim a0 As Byte = SourceBytes(Offset)
        Dim a1 As Byte = SourceBytes(Offset + 1)
        aPal(0) = a0
        aPal(1) = a1
        If a0 > a1 Then
            For i As Integer = 1 To 6
                aPal(i + 1) = CByte(((7 - i) * CInt(a0) + i * CInt(a1)) \ 7)
            Next
        Else
            For i As Integer = 1 To 4
                aPal(i + 1) = CByte(((5 - i) * CInt(a0) + i * CInt(a1)) \ 5)
            Next
            aPal(6) = 0
            aPal(7) = 255
        End If
        Dim AlphaTable As ULong = 0
        For i As Integer = 0 To 5
            AlphaTable = AlphaTable Or (CType(SourceBytes(Offset + 2 + i), ULong) << (i * 8))
        Next
        For i As Integer = 0 To 15
            Dim pX As Integer = xPixelBase + (i And 3)
            Dim pY As Integer = yPixelBase + (i >> 2)
            If pX < Width AndAlso pY < Height Then
                Dim destIdx As Integer = ((pY * Width + pX) * 4)
                Dim aIdx As Integer = CInt((AlphaTable >> (i * 3)) And 7UL)
                DecodedBytes(destIdx + TargetChannel) = aPal(aIdx)
                If TargetChannel <> 3 Then
                    DecodedBytes(destIdx + 3) = 255
                End If
            End If
        Next
    End Sub

    Private Sub DecodeBlockBC2(Offset As Integer, xPixelBase As Integer, yPixelBase As Integer)
        For byteIdx As Integer = 0 To 7
            Dim packedByte As Byte = SourceBytes(Offset + byteIdx)
            For nibble As Integer = 0 To 1
                Dim nibIdx As Integer = (byteIdx * 2) + nibble
                Dim pX As Integer = xPixelBase + (nibIdx And 3)
                Dim pY As Integer = yPixelBase + (nibIdx >> 2)
                If pX < Width AndAlso pY < Height Then
                    Dim destIdx As Integer = (pY * Width + pX) * 4
                    Dim alpha4Bit As Byte
                    If nibble = 0 Then
                        alpha4Bit = CByte(packedByte And &HF)
                    Else
                        alpha4Bit = CByte((packedByte >> 4) And &HF)
                    End If
                    DecodedBytes(destIdx + 3) = CByte((alpha4Bit << 4) Or alpha4Bit)
                End If
            Next
        Next
    End Sub

    Private Sub DecodeBlockBC1_Diagnostic(Offset As Integer, xPixelBase As Integer, yPixelBase As Integer, ActiveMode As Integer, ColorPalette() As Integer, Endpoints() As Integer)
        Dim Col0 As UShort = BitConverter.ToUInt16(SourceBytes, Offset)
        Dim Col1 As UShort = BitConverter.ToUInt16(SourceBytes, Offset + 2)
        Dim DiagVal As Byte = 0
        If ActiveMode >= 2 OrElse Col0 > Col1 Then
            DiagVal = 255
        End If
        For i As Integer = 0 To 15
            Dim pX As Integer = xPixelBase + (i And 3)
            Dim pY As Integer = yPixelBase + (i >> 2)
            If pX < Width AndAlso pY < Height Then
                Dim destIdx As Integer = (pY * Width + pX) * 4
                DecodedBytes(destIdx) = DiagVal
                DecodedBytes(destIdx + 1) = DiagVal
                DecodedBytes(destIdx + 2) = 255
                DecodedBytes(destIdx + 3) = 255
            End If
        Next
    End Sub

    Private Sub DecodeBlockBC1(Offset As Integer, xPixelBase As Integer, yPixelBase As Integer, ActiveMode As Integer, ColorPalette() As Integer, Endpoints() As Integer)
        Dim c0_raw As UShort = BitConverter.ToUInt16(SourceBytes, Offset)
        Dim c1_raw As UShort = BitConverter.ToUInt16(SourceBytes, Offset + 2)
        Dim ColorTable As UInteger = BitConverter.ToUInt32(SourceBytes, Offset + 4)
        Unpack565(c0_raw, Endpoints, 0)
        Unpack565(c1_raw, Endpoints, 3)
        ColorPalette(0) = Endpoints(0) : ColorPalette(1) = Endpoints(1) : ColorPalette(2) = Endpoints(2) : ColorPalette(3) = 255
        ColorPalette(4) = Endpoints(3) : ColorPalette(5) = Endpoints(4) : ColorPalette(6) = Endpoints(5) : ColorPalette(7) = 255
        If ActiveMode >= 2 OrElse c0_raw > c1_raw Then
            ColorPalette(8) = (ColorPalette(0) * 2 + ColorPalette(4)) \ 3
            ColorPalette(9) = (ColorPalette(1) * 2 + ColorPalette(5)) \ 3
            ColorPalette(10) = (ColorPalette(2) * 2 + ColorPalette(6)) \ 3
            ColorPalette(11) = 255
            ColorPalette(12) = (ColorPalette(0) + ColorPalette(4) * 2) \ 3
            ColorPalette(13) = (ColorPalette(1) + ColorPalette(5) * 2) \ 3
            ColorPalette(14) = (ColorPalette(2) + ColorPalette(6) * 2) \ 3
            ColorPalette(15) = 255
        Else
            ColorPalette(8) = (ColorPalette(0) + ColorPalette(4)) \ 2
            ColorPalette(9) = (ColorPalette(1) + ColorPalette(5)) \ 2
            ColorPalette(10) = (ColorPalette(2) + ColorPalette(6)) \ 2
            ColorPalette(11) = 255
            ColorPalette(12) = 0 : ColorPalette(13) = 0 : ColorPalette(14) = 0
            ColorPalette(15) = If(ActiveMode = 1, 0, 255)
        End If
        For i As Integer = 0 To 15
            Dim pX As Integer = xPixelBase + (i And 3)
            Dim pY As Integer = yPixelBase + (i >> 2)
            If pX < Width AndAlso pY < Height Then
                Dim cIdx As Integer = CInt((ColorTable >> (i * 2)) And 3UI) * 4
                Dim destIdx As Integer = (pY * Width + pX) * 4
                DecodedBytes(destIdx) = CByte(ColorPalette(cIdx))
                DecodedBytes(destIdx + 1) = CByte(ColorPalette(cIdx + 1))
                DecodedBytes(destIdx + 2) = CByte(ColorPalette(cIdx + 2))
                DecodedBytes(destIdx + 3) = CByte(ColorPalette(cIdx + 3))
            End If
        Next
    End Sub

#Region "BC7 Modes"

    Private Sub DecodeMode0(Offset As Integer, ByRef BitPos As Integer, xPixelBase As Integer, yPixelBase As Integer, R() As Integer, G() As Integer, B() As Integer, P() As Integer, subsetMap() As Integer, Anchors() As Integer)
        Dim Partition As Integer = ReadBits(Offset, BitPos, 4)
        For i As Integer = 0 To 5
            R(i) = ReadBits(Offset, BitPos, 4)
        Next
        For i As Integer = 0 To 5
            G(i) = ReadBits(Offset, BitPos, 4)
        Next
        For i As Integer = 0 To 5
            B(i) = ReadBits(Offset, BitPos, 4)
        Next
        For i As Integer = 0 To 5
            P(i) = ReadBits(Offset, BitPos, 1)
        Next
        For i As Integer = 0 To 5
            R(i) = (R(i) << 1) Or P(i) : R(i) = (R(i) << 3) Or (R(i) >> 2)
            G(i) = (G(i) << 1) Or P(i) : G(i) = (G(i) << 3) Or (G(i) >> 2)
            B(i) = (B(i) << 1) Or P(i) : B(i) = (B(i) << 3) Or (B(i) >> 2)
        Next
        Dim pTableVal As UInteger = PartitionTable3(Partition)
        Anchors(0) = 0 : Anchors(1) = AnchorIndexTable3_1(Partition) : Anchors(2) = AnchorIndexTable3_2(Partition)
        For i As Integer = 0 To 15
            Dim subset As Integer = CInt((pTableVal >> (i * 2)) And 3UI)
            subsetMap(i) = subset
        Next
        For i As Integer = 0 To 15
            Dim s As Integer = subsetMap(i)
            Dim numBits As Integer = 3
            If i = Anchors(0) OrElse i = Anchors(1) OrElse i = Anchors(2) Then
                numBits -= 1
            End If
            Dim index As Integer = ReadBits(Offset, BitPos, numBits)
            Dim w As Integer = Weight3(index)
            Dim iw As Integer = 64 - w
            Dim pX As Integer = xPixelBase + (i And 3)
            Dim pY As Integer = yPixelBase + (i >> 2)
            If pX < Width AndAlso pY < Height Then
                Dim destIdx As Integer = (pY * Width + pX) * 4
                DecodedBytes(destIdx) = CByte(((B(s * 2) * iw + B(s * 2 + 1) * w + 32) >> 6) And 255)
                DecodedBytes(destIdx + 1) = CByte(((G(s * 2) * iw + G(s * 2 + 1) * w + 32) >> 6) And 255)
                DecodedBytes(destIdx + 2) = CByte(((R(s * 2) * iw + R(s * 2 + 1) * w + 32) >> 6) And 255)
                DecodedBytes(destIdx + 3) = 255
            End If
        Next
    End Sub

    Private Sub DecodeMode1(Offset As Integer, ByRef BitPos As Integer, xPixelBase As Integer, yPixelBase As Integer, R() As Integer, G() As Integer, B() As Integer, P() As Integer, subsetMap() As Integer, Anchors() As Integer)
        Dim Partition As Integer = ReadBits(Offset, BitPos, 6)
        For i As Integer = 0 To 3
            R(i) = ReadBits(Offset, BitPos, 6)
        Next
        For i As Integer = 0 To 3
            G(i) = ReadBits(Offset, BitPos, 6)
        Next
        For i As Integer = 0 To 3
            B(i) = ReadBits(Offset, BitPos, 6)
        Next
        P(0) = ReadBits(Offset, BitPos, 1) : P(1) = ReadBits(Offset, BitPos, 1)
        For i As Integer = 0 To 3
            Dim pBit As Integer = P(i \ 2)
            R(i) = (R(i) << 1) Or pBit : R(i) = (R(i) << 1) Or (R(i) >> 6)
            G(i) = (G(i) << 1) Or pBit : G(i) = (G(i) << 1) Or (G(i) >> 6)
            B(i) = (B(i) << 1) Or pBit : B(i) = (B(i) << 1) Or (B(i) >> 6)
        Next
        Dim pTableVal As Integer = PartitionTable2(Partition)
        Anchors(0) = 0 : Anchors(1) = AnchorIndexTable2(Partition)
        For i As Integer = 0 To 15
            subsetMap(i) = (pTableVal >> i) And 1
        Next
        For i As Integer = 0 To 15
            Dim s As Integer = subsetMap(i)
            Dim numBits As Integer = 3
            If i = Anchors(0) OrElse i = Anchors(1) Then
                numBits -= 1
            End If
            Dim index As Integer = ReadBits(Offset, BitPos, numBits)
            Dim w As Integer = Weight3(index)
            Dim iw As Integer = 64 - w
            Dim pX As Integer = xPixelBase + (i And 3)
            Dim pY As Integer = yPixelBase + (i >> 2)
            If pX < Width AndAlso pY < Height Then
                Dim destIdx As Integer = (pY * Width + pX) * 4
                DecodedBytes(destIdx) = CByte(((B(s * 2) * iw + B(s * 2 + 1) * w + 32) >> 6) And 255)
                DecodedBytes(destIdx + 1) = CByte(((G(s * 2) * iw + G(s * 2 + 1) * w + 32) >> 6) And 255)
                DecodedBytes(destIdx + 2) = CByte(((R(s * 2) * iw + R(s * 2 + 1) * w + 32) >> 6) And 255)
                DecodedBytes(destIdx + 3) = 255
            End If
        Next
    End Sub

    Private Sub DecodeMode2(Offset As Integer, ByRef BitPos As Integer, xPixelBase As Integer, yPixelBase As Integer, R() As Integer, G() As Integer, B() As Integer, subsetMap() As Integer, Anchors() As Integer)
        Dim Partition As Integer = ReadBits(Offset, BitPos, 6)
        For i As Integer = 0 To 5
            R(i) = ReadBits(Offset, BitPos, 5)
        Next
        For i As Integer = 0 To 5
            G(i) = ReadBits(Offset, BitPos, 5)
        Next
        For i As Integer = 0 To 5
            B(i) = ReadBits(Offset, BitPos, 5)
        Next
        For i As Integer = 0 To 5
            R(i) = (R(i) << 3) Or (R(i) >> 2)
            G(i) = (G(i) << 3) Or (G(i) >> 2)
            B(i) = (B(i) << 3) Or (B(i) >> 2)
        Next
        Dim pTableVal As UInteger = PartitionTable3(Partition)
        Anchors(0) = 0 : Anchors(1) = AnchorIndexTable3_1(Partition) : Anchors(2) = AnchorIndexTable3_2(Partition)
        For i As Integer = 0 To 15
            Dim subset As Integer = CInt((pTableVal >> (i * 2)) And 3UI)
            subsetMap(i) = subset
        Next
        For i As Integer = 0 To 15
            Dim s As Integer = subsetMap(i)
            Dim numBits As Integer = 2
            If i = Anchors(0) OrElse i = Anchors(1) OrElse i = Anchors(2) Then
                numBits -= 1
            End If
            Dim index As Integer = ReadBits(Offset, BitPos, numBits)
            Dim w As Integer = Weight2(index)
            Dim iw As Integer = 64 - w
            Dim pX As Integer = xPixelBase + (i And 3)
            Dim pY As Integer = yPixelBase + (i >> 2)
            If pX < Width AndAlso pY < Height Then
                Dim destIdx As Integer = (pY * Width + pX) * 4
                DecodedBytes(destIdx) = CByte(((B(s * 2) * iw + B(s * 2 + 1) * w + 32) >> 6) And 255)
                DecodedBytes(destIdx + 1) = CByte(((G(s * 2) * iw + G(s * 2 + 1) * w + 32) >> 6) And 255)
                DecodedBytes(destIdx + 2) = CByte(((R(s * 2) * iw + R(s * 2 + 1) * w + 32) >> 6) And 255)
                DecodedBytes(destIdx + 3) = 255
            End If

        Next
    End Sub

    Private Sub DecodeMode3(Offset As Integer, ByRef BitPos As Integer, xPixelBase As Integer, yPixelBase As Integer, R() As Integer, G() As Integer, B() As Integer, P() As Integer, subsetMap() As Integer, Anchors() As Integer)
        Dim Partition As Integer = ReadBits(Offset, BitPos, 6)
        For i As Integer = 0 To 3
            R(i) = ReadBits(Offset, BitPos, 7)
        Next
        For i As Integer = 0 To 3
            G(i) = ReadBits(Offset, BitPos, 7)
        Next
        For i As Integer = 0 To 3
            B(i) = ReadBits(Offset, BitPos, 7)
        Next
        For i As Integer = 0 To 3
            P(i) = ReadBits(Offset, BitPos, 1)
        Next
        For i As Integer = 0 To 3
            R(i) = (R(i) << 1) Or P(i)
            G(i) = (G(i) << 1) Or P(i)
            B(i) = (B(i) << 1) Or P(i)
        Next
        Dim pTableVal As Integer = PartitionTable2(Partition)
        Anchors(0) = 0 : Anchors(1) = AnchorIndexTable2(Partition)
        For i As Integer = 0 To 15
            subsetMap(i) = (pTableVal >> i) And 1
        Next
        For i As Integer = 0 To 15
            Dim s As Integer = subsetMap(i)
            Dim numBits As Integer = 2
            If i = Anchors(0) OrElse i = Anchors(1) Then
                numBits -= 1
            End If
            Dim index As Integer = ReadBits(Offset, BitPos, numBits)
            Dim w As Integer = Weight2(index)
            Dim iw As Integer = 64 - w
            Dim pX As Integer = xPixelBase + (i And 3)
            Dim pY As Integer = yPixelBase + (i >> 2)
            If pX < Width AndAlso pY < Height Then
                Dim destIdx As Integer = (pY * Width + pX) * 4
                DecodedBytes(destIdx) = CByte(((B(s * 2) * iw + B(s * 2 + 1) * w + 32) >> 6) And 255)
                DecodedBytes(destIdx + 1) = CByte(((G(s * 2) * iw + G(s * 2 + 1) * w + 32) >> 6) And 255)
                DecodedBytes(destIdx + 2) = CByte(((R(s * 2) * iw + R(s * 2 + 1) * w + 32) >> 6) And 255)
                DecodedBytes(destIdx + 3) = 255
            End If
        Next
    End Sub

    Private Sub DecodeMode4(Offset As Integer, ByRef BitPos As Integer, xPixelBase As Integer, yPixelBase As Integer, Indices2Bit() As Integer, Indices3Bit() As Integer, ColorIndices() As Integer, AlphaIndices() As Integer)
        Dim Rotation As Integer = ReadBits(Offset, BitPos, 2)
        Dim IndexMode As Integer = ReadBits(Offset, BitPos, 1)
        Dim r0 As Integer = ReadBits(Offset, BitPos, 5) : Dim r1 As Integer = ReadBits(Offset, BitPos, 5)
        Dim g0 As Integer = ReadBits(Offset, BitPos, 5) : Dim g1 As Integer = ReadBits(Offset, BitPos, 5)
        Dim b0 As Integer = ReadBits(Offset, BitPos, 5) : Dim b1 As Integer = ReadBits(Offset, BitPos, 5)
        Dim a0 As Integer = ReadBits(Offset, BitPos, 6) : Dim a1 As Integer = ReadBits(Offset, BitPos, 6)
        r0 = (r0 << 3) Or (r0 >> 2) : r1 = (r1 << 3) Or (r1 >> 2)
        g0 = (g0 << 3) Or (g0 >> 2) : g1 = (g1 << 3) Or (g1 >> 2)
        b0 = (b0 << 3) Or (b0 >> 2) : b1 = (b1 << 3) Or (b1 >> 2)
        a0 = (a0 << 2) Or (a0 >> 4) : a1 = (a1 << 2) Or (a1 >> 4)
        Dim ColorBits As Integer = If(IndexMode = 0, 2, 3)
        Dim AlphaBits As Integer = If(IndexMode = 0, 3, 2)
        For i As Integer = 0 To 15
            Indices2Bit(i) = ReadBits(Offset, BitPos, If(i = 0, 1, 2))
        Next
        For i As Integer = 0 To 15
            Indices3Bit(i) = ReadBits(Offset, BitPos, If(i = 0, 2, 3))
        Next
        If IndexMode = 0 Then
            ColorIndices = Indices2Bit
            AlphaIndices = Indices3Bit
        Else
            ColorIndices = Indices3Bit
            AlphaIndices = Indices2Bit
        End If
        For i As Integer = 0 To 15
            Dim cw As Integer = If(IndexMode = 0, Weight2(ColorIndices(i)), Weight3(ColorIndices(i)))
            Dim icw As Integer = 64 - cw
            Dim aw As Integer = If(IndexMode = 0, Weight3(AlphaIndices(i)), Weight2(AlphaIndices(i)))
            Dim iaw As Integer = 64 - aw
            Dim r As Integer = ((r0 * icw + r1 * cw + 32) >> 6) And 255
            Dim g As Integer = ((g0 * icw + g1 * cw + 32) >> 6) And 255
            Dim b As Integer = ((b0 * icw + b1 * cw + 32) >> 6) And 255
            Dim a As Integer = ((a0 * iaw + a1 * aw + 32) >> 6) And 255
            Select Case Rotation
                Case 1 : Dim t As Integer = r : r = a : a = t
                Case 2 : Dim t As Integer = g : g = a : a = t
                Case 3 : Dim t As Integer = b : b = a : a = t
            End Select
            Dim pX As Integer = xPixelBase + (i And 3)
            Dim pY As Integer = yPixelBase + (i >> 2)
            If pX < Width AndAlso pY < Height Then
                Dim destIdx As Integer = (pY * Width + pX) * 4
                DecodedBytes(destIdx) = CByte(b)
                DecodedBytes(destIdx + 1) = CByte(g)
                DecodedBytes(destIdx + 2) = CByte(r)
                DecodedBytes(destIdx + 3) = CByte(a)
            End If
        Next
    End Sub


    Private Sub DecodeMode5(Offset As Integer, ByRef BitPos As Integer, xPixelBase As Integer, yPixelBase As Integer, ColorIndices() As Integer, AlphaIndices() As Integer)
        Dim Rotation As Integer = ReadBits(Offset, BitPos, 2)
        Dim r0 As Integer = ReadBits(Offset, BitPos, 7) : Dim r1 As Integer = ReadBits(Offset, BitPos, 7)
        Dim g0 As Integer = ReadBits(Offset, BitPos, 7) : Dim g1 As Integer = ReadBits(Offset, BitPos, 7)
        Dim b0 As Integer = ReadBits(Offset, BitPos, 7) : Dim b1 As Integer = ReadBits(Offset, BitPos, 7)
        Dim a0 As Integer = ReadBits(Offset, BitPos, 8) : Dim a1 As Integer = ReadBits(Offset, BitPos, 8)
        r0 = (r0 << 1) Or (r0 >> 6) : r1 = (r1 << 1) Or (r1 >> 6)
        g0 = (g0 << 1) Or (g0 >> 6) : g1 = (g1 << 1) Or (g1 >> 6)
        b0 = (b0 << 1) Or (b0 >> 6) : b1 = (b1 << 1) Or (b1 >> 6)
        For i As Integer = 0 To 15
            ColorIndices(i) = ReadBits(Offset, BitPos, If(i = 0, 1, 2))
        Next
        For i As Integer = 0 To 15
            AlphaIndices(i) = ReadBits(Offset, BitPos, If(i = 0, 1, 2))
        Next
        For i As Integer = 0 To 15
            Dim cw As Integer = Weight2(ColorIndices(i))
            Dim icw As Integer = 64 - cw
            Dim aw As Integer = Weight2(AlphaIndices(i))
            Dim iaw As Integer = 64 - aw
            Dim r As Integer = ((r0 * icw + r1 * cw + 32) >> 6) And 255
            Dim g As Integer = ((g0 * icw + g1 * cw + 32) >> 6) And 255
            Dim b As Integer = ((b0 * icw + b1 * cw + 32) >> 6) And 255
            Dim a As Integer = ((a0 * iaw + a1 * aw + 32) >> 6) And 255
            Select Case Rotation
                Case 1 : Dim t As Integer = r : r = a : a = t
                Case 2 : Dim t As Integer = g : g = a : a = t
                Case 3 : Dim t As Integer = b : b = a : a = t
            End Select
            Dim pX As Integer = xPixelBase + (i And 3)
            Dim pY As Integer = yPixelBase + (i >> 2)
            If pX < Width AndAlso pY < Height Then
                Dim destIdx As Integer = (pY * Width + pX) * 4
                DecodedBytes(destIdx) = CByte(b)
                DecodedBytes(destIdx + 1) = CByte(g)
                DecodedBytes(destIdx + 2) = CByte(r)
                DecodedBytes(destIdx + 3) = CByte(a)
            End If
        Next
    End Sub

    Private Sub DecodeMode6(Offset As Integer, ByRef BitPos As Integer, xPixelBase As Integer, yPixelBase As Integer)
        Dim r0 As Integer = ReadBits(Offset, BitPos, 7) : Dim r1 As Integer = ReadBits(Offset, BitPos, 7)
        Dim g0 As Integer = ReadBits(Offset, BitPos, 7) : Dim g1 As Integer = ReadBits(Offset, BitPos, 7)
        Dim b0 As Integer = ReadBits(Offset, BitPos, 7) : Dim b1 As Integer = ReadBits(Offset, BitPos, 7)
        Dim a0 As Integer = ReadBits(Offset, BitPos, 7) : Dim a1 As Integer = ReadBits(Offset, BitPos, 7)
        Dim p0 As Integer = ReadBits(Offset, BitPos, 1) : Dim p1 As Integer = ReadBits(Offset, BitPos, 1)
        r0 = (r0 << 1) Or p0 : r1 = (r1 << 1) Or p1
        g0 = (g0 << 1) Or p0 : g1 = (g1 << 1) Or p1
        b0 = (b0 << 1) Or p0 : b1 = (b1 << 1) Or p1
        a0 = (a0 << 1) Or p0 : a1 = (a1 << 1) Or p1
        For i As Integer = 0 To 15
            Dim Index As Integer = ReadBits(Offset, BitPos, If(i = 0, 3, 4))
            Dim w As Integer = Weight4(Index)
            Dim iw As Integer = 64 - w
            Dim pX As Integer = xPixelBase + (i And 3)
            Dim pY As Integer = yPixelBase + (i >> 2)
            If pX < Width AndAlso pY < Height Then
                Dim destIdx As Integer = (pY * Width + pX) * 4
                DecodedBytes(destIdx) = CByte(((b0 * iw + b1 * w + 32) >> 6) And 255)
                DecodedBytes(destIdx + 1) = CByte(((g0 * iw + g1 * w + 32) >> 6) And 255)
                DecodedBytes(destIdx + 2) = CByte(((r0 * iw + r1 * w + 32) >> 6) And 255)
                DecodedBytes(destIdx + 3) = CByte(((a0 * iw + a1 * w + 32) >> 6) And 255)
            End If
        Next
    End Sub

    Private Sub DecodeMode7(Offset As Integer, ByRef BitPos As Integer, xPixelBase As Integer, yPixelBase As Integer, A() As Integer, R() As Integer, G() As Integer, B() As Integer, P() As Integer, subsetMap() As Integer, Anchors() As Integer)
        Dim Partition As Integer = ReadBits(Offset, BitPos, 6)
        For i As Integer = 0 To 3
            R(i) = ReadBits(Offset, BitPos, 5)
        Next
        For i As Integer = 0 To 3
            G(i) = ReadBits(Offset, BitPos, 5)
        Next
        For i As Integer = 0 To 3
            B(i) = ReadBits(Offset, BitPos, 5)
        Next
        For i As Integer = 0 To 3
            A(i) = ReadBits(Offset, BitPos, 5)
        Next
        For i As Integer = 0 To 3
            P(i) = ReadBits(Offset, BitPos, 1)
        Next
        For i As Integer = 0 To 3
            R(i) = (R(i) << 1) Or P(i) : R(i) = (R(i) << 2) Or (R(i) >> 4)
            G(i) = (G(i) << 1) Or P(i) : G(i) = (G(i) << 2) Or (G(i) >> 4)
            B(i) = (B(i) << 1) Or P(i) : B(i) = (B(i) << 2) Or (B(i) >> 4)
            A(i) = (A(i) << 1) Or P(i) : A(i) = (A(i) << 2) Or (A(i) >> 4)
        Next
        Dim pTableVal As Integer = PartitionTable2(Partition)
        Anchors = {0, AnchorIndexTable2(Partition)}
        For i As Integer = 0 To 15
            subsetMap(i) = (pTableVal >> i) And 1
        Next
        For i As Integer = 0 To 15
            Dim s As Integer = subsetMap(i)
            Dim numBits As Integer = 2
            If i = Anchors(0) OrElse i = Anchors(1) Then
                numBits -= 1
            End If
            Dim index As Integer = ReadBits(Offset, BitPos, numBits)
            Dim w As Integer = Weight2(index)
            Dim iw As Integer = 64 - w
            Dim pX As Integer = xPixelBase + (i And 3)
            Dim pY As Integer = yPixelBase + (i >> 2)
            If pX < Width AndAlso pY < Height Then
                Dim destIdx As Integer = (pY * Width + pX) * 4
                DecodedBytes(destIdx) = CByte(((B(s * 2) * iw + B(s * 2 + 1) * w + 32) >> 6) And 255)
                DecodedBytes(destIdx + 1) = CByte(((G(s * 2) * iw + G(s * 2 + 1) * w + 32) >> 6) And 255)
                DecodedBytes(destIdx + 2) = CByte(((R(s * 2) * iw + R(s * 2 + 1) * w + 32) >> 6) And 255)
                DecodedBytes(destIdx + 3) = CByte(((A(s * 2) * iw + A(s * 2 + 1) * w + 32) >> 6) And 255)
            End If
        Next
    End Sub

    ''' <summary>
    ''' Replaces the specific block with a solid color or partitioned set of colors, useful for debugging.
    ''' </summary>
    Private Sub DecodeModeDiagnostic(BlockOffset As Integer, xPixelBase As Integer, yPixelBase As Integer)
        Dim firstByte As Byte = SourceBytes(BlockOffset)
        Dim mode As Integer = ModeLUT(firstByte)
        Dim BaseR() As Integer = {0, 0, 255, 255, 255, 0, 255, 64}
        Dim BaseG() As Integer = {0, 255, 0, 255, 0, 255, 255, 64}
        Dim BaseB() As Integer = {255, 0, 0, 0, 255, 255, 255, 64}
        Dim Intensity() As Double = {1.0, 0.6, 0.3}
        Dim numSubsets As Integer = 1
        Dim partitionID As Integer = -1
        Dim bitPosition As Integer = 0
        If mode = 0 Then
            numSubsets = 3
            bitPosition = 1
            partitionID = ReadBits(BlockOffset, bitPosition, 4)
        ElseIf mode = 1 Then
            numSubsets = 2
            bitPosition = 2
            partitionID = ReadBits(BlockOffset, bitPosition, 6)
        ElseIf mode = 2 Then
            numSubsets = 3
            bitPosition = 3
            partitionID = ReadBits(BlockOffset, bitPosition, 6)
        ElseIf mode = 3 Then
            numSubsets = 2
            bitPosition = 4
            partitionID = ReadBits(BlockOffset, bitPosition, 6)
        ElseIf mode = 7 Then
            numSubsets = 2
            bitPosition = 8
            partitionID = ReadBits(BlockOffset, bitPosition, 6)
        End If
        For i As Integer = 0 To 15
            Dim pX As Integer = xPixelBase + (i And 3)
            Dim pY As Integer = yPixelBase + (i >> 2)
            If pX < Width AndAlso pY < Height Then
                Dim destIdx As Integer = (pY * Width + pX) * 4
                Dim currentSubset As Integer = 0
                If partitionID >= 0 Then
                    If numSubsets = 2 Then
                        Dim mask As Integer = 1 << i
                        If (PartitionTable2(partitionID) And mask) <> 0 Then currentSubset = 1
                    ElseIf numSubsets = 3 Then
                        Dim shift As Integer = i * 2
                        currentSubset = CInt((PartitionTable3(partitionID) >> shift) And 3UI)
                    End If
                End If
                Dim lum As Double = Intensity(currentSubset)
                DecodedBytes(destIdx) = CByte(BaseB(mode) * lum)
                DecodedBytes(destIdx + 1) = CByte(BaseG(mode) * lum)
                DecodedBytes(destIdx + 2) = CByte(BaseR(mode) * lum)
                DecodedBytes(destIdx + 3) = 255
            End If
        Next
    End Sub

    Private Function ReadBits(Offset As Integer, ByRef BitPosition As Integer, BitCount As Integer) As Integer
        Dim Value As Integer = 0
        Dim BitsRead As Integer = 0
        While BitsRead < BitCount
            Dim ByteIdx As Integer = BitPosition >> 3
            Dim BitInByte As Integer = BitPosition And 7
            Dim BitsToReadFromByte As Integer = Math.Min(BitCount - BitsRead, 8 - BitInByte)
            Dim Mask As Integer = (1 << BitsToReadFromByte) - 1
            Dim Bits As Integer = (SourceBytes(Offset + ByteIdx) >> BitInByte) And Mask
            Value = Value Or (Bits << BitsRead)
            BitPosition += BitsToReadFromByte
            BitsRead += BitsToReadFromByte
        End While
        Return Value
    End Function

#End Region

    Public Sub Save(Path As String, FileExt As String)
        Dim bmpSource As BitmapSource = ToBitmapSource()
        Dim encoder As BitmapEncoder = GetEncoderFromExtension(FileExt)
        encoder.Frames.Add(BitmapFrame.Create(bmpSource))
        Using fs As New FileStream(Path, FileMode.Create, FileAccess.Write)
            encoder.Save(fs)
        End Using
    End Sub

    Public Function ToBitmapSource() As BitmapSource
        BeginDecode()
        Dim Stride As Integer = Width * 4
        Dim WICBitmap As New WriteableBitmap(Width, Height, 96.0, 96.0, OutputPixelFormat, Nothing)
        Dim destRect As New Windows.Int32Rect(0, 0, Width, Height)
        WICBitmap.WritePixels(destRect, DecodedBytes, Stride, 0)
        WICBitmap.Freeze()
        Return WICBitmap
    End Function

    Public Sub SaveCubeMaps(Path As String, FileExt As String)
        Dim bmps = ToCubeBitmapSources()
        Dim basePath As String = Path.Substring(0, Path.LastIndexOf("."c))
        For i As Integer = 0 To 5
            If bmps(i) IsNot Nothing Then
                Dim encoder As BitmapEncoder = GetEncoderFromExtension(FileExt)
                encoder.Frames.Add(BitmapFrame.Create(bmps(i)))
                Dim outPath = $"{basePath}{CubeSuffixes(i)}{FileExt}"
                Using fs As New FileStream(outPath, FileMode.Create, FileAccess.Write)
                    encoder.Save(fs)
                End Using
            End If
        Next
    End Sub

    Public Function ToCubeBitmapSources() As BitmapSource()
        DecodeCubeMap()
        Dim bmps(5) As BitmapSource
        Dim Stride As Integer = Width * 4
        For i As Integer = 0 To 5
            If CubeFaces(i) IsNot Nothing Then
                Dim bmp As BitmapSource = BitmapSource.Create(Width, Height, 96.0, 96.0, OutputPixelFormat, Nothing, CubeFaces(i), Stride)
                bmp.Freeze()
                bmps(i) = bmp
            End If
        Next
        Return bmps
    End Function

    Private Sub SetMask(RIdx As Integer, GIdx As Integer, BIdx As Integer, Optional AIdx As Integer = -1)
        RedBitMask = If(RIdx >= 0, &HFF << (RIdx * 8), 0)
        GreenBitMask = If(GIdx >= 0, &HFF << (GIdx * 8), 0)
        BlueBitMask = If(BIdx >= 0, &HFF << (BIdx * 8), 0)
        AlphaBitMask = If(AIdx >= 0, &HFF << (AIdx * 8), 0)
    End Sub

    Private Sub Unpack565(PackedColor As UShort, ByRef Buffer() As Integer, Optional StartOffset As Integer = 0)
        Dim r5 As Integer = (PackedColor >> 11) And 31
        Dim g6 As Integer = (PackedColor >> 5) And 63
        Dim b5 As Integer = PackedColor And 31
        Buffer(StartOffset) = (b5 << 3) Or (b5 >> 2)
        Buffer(StartOffset + 1) = (g6 << 2) Or (g6 >> 4)
        Buffer(StartOffset + 2) = (r5 << 3) Or (r5 >> 2)
    End Sub

    Private Function GetShiftCount(Mask As UInteger) As Integer
        If Mask = 0 Then Return 0
        Dim Shift As Integer = 0
        Dim TempMask As UInteger = Mask
        While (TempMask And 1UI) = 0
            TempMask >>= 1
            Shift += 1
            If Shift > 32 Then Return 0
        End While
        Return Shift
    End Function

    Private Function GetEncoderFromExtension(FileExt As String) As BitmapEncoder
        Select Case FileExt.ToLower()
            Case ".png" : Return New PngBitmapEncoder()
            Case ".jpg", ".jpeg" : Return New JpegBitmapEncoder()
            Case ".bmp" : Return New BmpBitmapEncoder()
            Case Else : Return New PngBitmapEncoder() ' Fallback default
        End Select
    End Function

    Private Function GetFileBytes(Source As String, Offset As Long, Count As Integer) As Byte()
        Dim Buffer() As Byte = New Byte(Count - 1) {}
        Using FileReader As New FileStream(Source, FileMode.Open, FileAccess.Read, FileShare.Read)
            FileReader.Seek(Offset, SeekOrigin.Begin)
            Dim totalBytesRead As Integer = 0
            While totalBytesRead < Count
                Dim bytesRead As Integer = FileReader.Read(Buffer, totalBytesRead, Count - totalBytesRead)
                If bytesRead = 0 Then
                    Throw New EndOfStreamException($"Unexpected end of file. Expected {Count} bytes, but only found {totalBytesRead}.")
                End If
                totalBytesRead += bytesRead
            End While
        End Using
        Return Buffer
    End Function

    Private Function GetFileBytes(Source As Stream, Offset As Long, Count As Integer) As Byte()
        Dim Buffer() As Byte = New Byte(Count - 1) {}
        Source.Seek(Offset, SeekOrigin.Begin)
        Dim totalBytesRead As Integer = 0
        While totalBytesRead < Count
            Dim bytesRead As Integer = Source.Read(Buffer, totalBytesRead, Count - totalBytesRead)
            If bytesRead = 0 Then
                Throw New EndOfStreamException($"Unexpected end of file. Expected {Count} bytes, but only found {totalBytesRead}.")
            End If
            totalBytesRead += bytesRead
        End While
        Return Buffer
    End Function

    Protected Overridable Sub Dispose(Disposing As Boolean)
        If Not Disposed Then
            SourceBytes = Nothing
            EncodedBytes = Nothing
            DecodedBytes = Nothing
        End If
        Disposed = True
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose(True)
        GC.SuppressFinalize(Me)
    End Sub

End Class
