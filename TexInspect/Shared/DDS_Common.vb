' DDS Common Class by WalkerMx
' Based on the documentation found here:
' http://doc.51windows.net/directx9_sdk/graphics/reference/DDSFileReference/ddsfileformat.htm
' https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dx-graphics-dds

Public Module DDS_Common

    Public Options As New ParallelOptions With {.MaxDegreeOfParallelism = Environment.ProcessorCount}
    Public WicAlphaFormats As PixelFormat() = {PixelFormats.Bgra32, PixelFormats.Pbgra32, PixelFormats.Rgba64, PixelFormats.Prgba64, PixelFormats.Rgba128Float}

    <Flags>
    Public Enum DDS_SurfaceFlags
        DDSD_CAPS = &H1
        DDSD_HEIGHT = &H2
        DDSD_WIDTH = &H4
        DDSD_PITCH = &H8
        DDSD_PIXELFORMAT = &H1000
        DDSD_MIPMAPCOUNT = &H20000
        DDSD_LINEARSIZE = &H80000
        DDSD_DEPTH = &H800000
    End Enum

    <Flags>
    Public Enum DDS_PixelFlags
        DDPF_ALPHAPIXELS = &H1
        DDPF_ALPHA = &H2
        DDPF_FOURCC = &H4
        DDPF_RGB = &H40
        DDPF_YUV = &H200
        DDPF_LUMINANCE = &H20000
    End Enum

    <Flags>
    Public Enum DDS_Caps1
        DDSCAPS_COMPLEX = &H8
        DDSCAPS_TEXTURE = &H1000
        DDSCAPS_MIPMAP = &H400000
    End Enum

    <Flags>
    Public Enum DDS_Caps2
        DDSCAPS2_CUBEMAP = &H200
        DDSCAPS2_CUBEMAP_POSITIVEX = &H400
        DDSCAPS2_CUBEMAP_NEGATIVEX = &H800
        DDSCAPS2_CUBEMAP_POSITIVEY = &H1000
        DDSCAPS2_CUBEMAP_NEGATIVEY = &H2000
        DDSCAPS2_CUBEMAP_POSITIVEZ = &H4000
        DDSCAPS2_CUBEMAP_NEGATIVEZ = &H8000
        DDSCAPS2_VOLUME = &H200000
    End Enum

    Public Enum DXGI_Format
        DXGI_FORMAT_UNKNOWN = &H0
        DXGI_FORMAT_R32G32B32A32_TYPELESS = &H1
        DXGI_FORMAT_R32G32B32A32_FLOAT = &H2
        DXGI_FORMAT_R32G32B32A32_UINT = &H3
        DXGI_FORMAT_R32G32B32A32_SINT = &H4
        DXGI_FORMAT_R16G16B16A16_TYPELESS = &H9
        DXGI_FORMAT_R16G16B16A16_FLOAT = &HA
        DXGI_FORMAT_R16G16B16A16_UNORM = &HB
        DXGI_FORMAT_R16G16B16A16_UINT = &HC
        DXGI_FORMAT_R16G16B16A16_SNORM = &HD
        DXGI_FORMAT_R16G16B16A16_SINT = &HE
        DXGI_FORMAT_R8G8B8A8_TYPELESS = &H1B
        DXGI_FORMAT_R8G8B8A8_UNORM = &H1C
        DXGI_FORMAT_R8G8B8A8_UNORM_SRGB = &H1D
        DXGI_FORMAT_R8G8B8A8_UINT = &H1E
        DXGI_FORMAT_R8G8B8A8_SNORM = &H1F
        DXGI_FORMAT_R8G8B8A8_SINT = &H20
        DXGI_FORMAT_BC1_TYPELESS = &H46
        DXGI_FORMAT_BC1_UNORM = &H47
        DXGI_FORMAT_BC1_UNORM_SRGB = &H48
        DXGI_FORMAT_BC2_TYPELESS = &H49
        DXGI_FORMAT_BC2_UNORM = &H4A
        DXGI_FORMAT_BC2_UNORM_SRGB = &H4B
        DXGI_FORMAT_BC3_TYPELESS = &H4C
        DXGI_FORMAT_BC3_UNORM = &H4D
        DXGI_FORMAT_BC3_UNORM_SRGB = &H4E
        DXGI_FORMAT_BC4_TYPELESS = &H4F
        DXGI_FORMAT_BC4_UNORM = &H50
        DXGI_FORMAT_BC4_SNORM = &H51
        DXGI_FORMAT_BC5_TYPELESS = &H52
        DXGI_FORMAT_BC5_UNORM = &H53
        DXGI_FORMAT_BC5_SNORM = &H54
        DXGI_FORMAT_B8G8R8A8_UNORM = &H57
        DXGI_FORMAT_B8G8R8X8_UNORM = &H58
        DXGI_FORMAT_B8G8R8A8_TYPELESS = &H5A
        DXGI_FORMAT_B8G8R8A8_UNORM_SRGB = &H5B
        DXGI_FORMAT_B8G8R8X8_TYPELESS = &H5C
        DXGI_FORMAT_B8G8R8X8_UNORM_SRGB = &H5D
        DXGI_FORMAT_BC6H_TYPELESS = &H5E
        DXGI_FORMAT_BC6H_UF16 = &H5F
        DXGI_FORMAT_BC6H_SF16 = &H60
        DXGI_FORMAT_BC7_TYPELESS = &H61
        DXGI_FORMAT_BC7_UNORM = &H62
        DXGI_FORMAT_BC7_UNORM_SRGB = &H63
    End Enum

    <Flags>
    Public Enum DX10_ResourceDimension As Integer
        D3D10_RESOURCE_DIMENSION_UNKNOWN = &H0
        D3D10_RESOURCE_DIMENSION_BUFFER = &H1
        D3D10_RESOURCE_DIMENSION_TEXTURE1D = &H2
        D3D10_RESOURCE_DIMENSION_TEXTURE2D = &H3
        D3D10_RESOURCE_DIMENSION_TEXTURE3D = &H4
    End Enum

    <Flags>
    Public Enum DX10_MiscFlags As Integer
        D3D10_RESOURCE_MISC_NONE = &H0
        D3D10_RESOURCE_MISC_TEXTURECUBE = &H4
    End Enum

    Public Enum DX10_MiscFlags2 As Integer
        DDS_ALPHA_MODE_UNKNOWN = &H0
        DDS_ALPHA_MODE_STRAIGHT = &H1
        DDS_ALPHA_MODE_PREMULTIPLIED = &H2
        DDS_ALPHA_MODE_OPAQUE = &H3
        DDS_ALPHA_MODE_CUSTOM = &H4
    End Enum

    Public Enum DDS_SpecialFlags As Integer
        DDS_DXT1o = &H1
        DDS_DXT2 = &H2
        DDS_DXT4 = &H3
        DDS_DXT5n = &H4
        DDS_BC7n = &H5
    End Enum

    ' BC7 Mode 1/7 Heuristic Tables (2-Subset)
    Public ReadOnly BlockCorners() As Integer = {0, 3, 12, 15}
    Public ReadOnly PartitionMap() As Integer = {-1, 7, 21, 13, 19, 0, 35, 6}

    ' Catmull-Rom Weight Table
    Public ReadOnly Weight4x4() As Integer = {1, -9, -9, 1, -9, 81, 81, -9, -9, 81, 81, -9, 1, -9, -9, 1}

    ' Cubemap File Extension Table
    Public ReadOnly CubeSuffixes As String() = {"_PX", "_NX", "_PY", "_NY", "_PZ", "_NZ"}

    ' BC7 Mode, Weight, Anchor, and Parition Tables
    Public ReadOnly Weight2() As Integer = {0, 21, 43, 64}
    Public ReadOnly Weight3() As Integer = {0, 9, 18, 27, 37, 46, 55, 64}
    Public ReadOnly Weight4() As Integer = {0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64}

    Public ReadOnly ModeLUT() As Integer = {
        8, 0, 1, 0, 2, 0, 1, 0, 3, 0, 1, 0, 2, 0, 1, 0,
        4, 0, 1, 0, 2, 0, 1, 0, 3, 0, 1, 0, 2, 0, 1, 0,
        5, 0, 1, 0, 2, 0, 1, 0, 3, 0, 1, 0, 2, 0, 1, 0,
        4, 0, 1, 0, 2, 0, 1, 0, 3, 0, 1, 0, 2, 0, 1, 0,
        6, 0, 1, 0, 2, 0, 1, 0, 3, 0, 1, 0, 2, 0, 1, 0,
        4, 0, 1, 0, 2, 0, 1, 0, 3, 0, 1, 0, 2, 0, 1, 0,
        5, 0, 1, 0, 2, 0, 1, 0, 3, 0, 1, 0, 2, 0, 1, 0,
        4, 0, 1, 0, 2, 0, 1, 0, 3, 0, 1, 0, 2, 0, 1, 0,
        7, 0, 1, 0, 2, 0, 1, 0, 3, 0, 1, 0, 2, 0, 1, 0,
        4, 0, 1, 0, 2, 0, 1, 0, 3, 0, 1, 0, 2, 0, 1, 0,
        5, 0, 1, 0, 2, 0, 1, 0, 3, 0, 1, 0, 2, 0, 1, 0,
        4, 0, 1, 0, 2, 0, 1, 0, 3, 0, 1, 0, 2, 0, 1, 0,
        6, 0, 1, 0, 2, 0, 1, 0, 3, 0, 1, 0, 2, 0, 1, 0,
        4, 0, 1, 0, 2, 0, 1, 0, 3, 0, 1, 0, 2, 0, 1, 0,
        5, 0, 1, 0, 2, 0, 1, 0, 3, 0, 1, 0, 2, 0, 1, 0,
        4, 0, 1, 0, 2, 0, 1, 0, 3, 0, 1, 0, 2, 0, 1, 0}

    Public ReadOnly AnchorIndexTable2() As Integer = {
        15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15,
        15, 2, 8, 2, 2, 8, 8, 15, 2, 8, 2, 2, 8, 8, 2, 2,
        15, 15, 6, 8, 2, 8, 15, 15, 2, 8, 2, 2, 2, 15, 15, 6,
        6, 2, 6, 8, 15, 15, 2, 2, 15, 15, 15, 15, 15, 2, 2, 15}

    Public ReadOnly AnchorIndexTable3_1() As Integer = {
        3, 3, 15, 15, 8, 3, 15, 15, 8, 8, 6, 6, 6, 5, 3, 3,
        3, 3, 8, 15, 3, 3, 6, 10, 5, 8, 8, 6, 8, 5, 12, 12,
        8, 8, 5, 5, 3, 15, 3, 5, 2, 10, 6, 6, 10, 8, 5, 5,
        1, 1, 4, 5, 5, 4, 1, 9, 3, 6, 5, 5, 5, 8, 5, 10}

    Public ReadOnly AnchorIndexTable3_2() As Integer = {
        15, 8, 8, 3, 15, 15, 3, 8, 15, 15, 15, 15, 15, 15, 15, 8,
        15, 8, 15, 3, 15, 8, 15, 8, 3, 15, 6, 10, 15, 15, 10, 8,
        15, 3, 15, 10, 10, 8, 9, 10, 6, 15, 8, 15, 3, 6, 6, 8,
        12, 8, 3, 15, 1, 3, 12, 8, 15, 2, 15, 15, 3, 15, 15, 8}

    Public ReadOnly PartitionTable2() As Integer = {
        &HCCCC, &H8888, &HEEEE, &HECC8, &HC880, &HFEEC, &HFEC8, &HEC80,
        &HC800, &HFFEC, &HFE80, &HE800, &HFFE8, &HFF00, &HFFF0, &HF000,
        &HF710, &H8E, &H7100, &H8CE, &H8C, &H7310, &H3100, &H8CCE,
        &H88C, &H3110, &H6666, &H366C, &H17E8, &HFF0, &H718E, &H399C,
        &HAAAA, &HF0F0, &H5A5A, &H33CC, &H3C3C, &H55AA, &H9696, &HA55A,
        &H73CE, &H13C8, &H324C, &H3BDC, &H6996, &HC33C, &H9966, &H660,
        &H272, &H4E4, &H4E40, &H2720, &HC936, &H936C, &H39C6, &H639C,
        &H9336, &H9CC6, &H817E, &HE718, &HCCF0, &HFCC, &H7744, &HEE22}

    Public ReadOnly PartitionTable3() As UInteger = {
        &HAA685050UI, &H6A5A5040UI, &H5A5A4200UI, &H5450A0A8UI, &HA5A50000UI, &HA0A05050UI, &H5555A0A0UI, &H5A5A5050UI,
        &HAA550000UI, &HAA555500UI, &HAAAA5500UI, &H90909090UI, &H94949494UI, &HA4A4A4A4UI, &HA9A59450UI, &H2A0A4250UI,
        &HA5945040UI, &HA425054UI, &HA5A5A500UI, &H55A0A0A0UI, &HA8A85454UI, &H6A6A4040UI, &HA4A45000UI, &H1A1A0500UI,
        &H50A4A4UI, &HAAA59090UI, &H14696914UI, &H69691400UI, &HA08585A0UI, &HAA821414UI, &H50A4A450UI, &H6A5A0200UI,
        &HA9A58000UI, &H5090A0A8UI, &HA8A09050UI, &H24242424UI, &HAA5500UI, &H24924924UI, &H24499224UI, &H50A50A50UI,
        &H500AA550UI, &HAAAA4444UI, &H66660000UI, &HA5A0A5A0UI, &H50A050A0UI, &H69286928UI, &H44AAAA44UI, &H66666600UI,
        &HAA444444UI, &H54A854A8UI, &H95809580UI, &H96969600UI, &HA85454A8UI, &H80959580UI, &HAA141414UI, &H96960000UI,
        &HAAAA1414UI, &HA05050A0UI, &HA0A5A5A0UI, &H96000000UI, &H40804080UI, &HA9A8A9A8UI, &HAAAAAA44UI, &H2A4A5254UI}

    Public Function LoadBitmapSource(filePath As String) As BitmapSource
        Dim bmp As New BitmapImage()
        bmp.BeginInit()
        bmp.CacheOption = BitmapCacheOption.OnLoad
        bmp.UriSource = New Uri(filePath)
        bmp.EndInit()
        bmp.Freeze()
        Return bmp
    End Function

End Module
