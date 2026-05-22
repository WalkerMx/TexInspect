Imports System.IO

Public Class CubeSlicer
    Public Width As Integer
    Public Height As Integer
    Public FaceSize As Integer
    Public Layout As ImageLayout
    Public FilePath As String

    Public CubeBitmaps As New Dictionary(Of String, BitmapSource)
    Private FaceCoordinates As New Dictionary(Of String, (X As Integer, Y As Integer))

    Public Enum ImageLayout
        StripHorizontal = 0
        StripVertical = 1
        CrossHorizontal = 2
        CrossVertical = 3
    End Enum

    Public Sub New(SourcePath As String)
        FilePath = SourcePath
        Dim Source As BitmapSource = LoadBitmapSource(FilePath)
        Width = Source.PixelWidth
        Height = Source.PixelHeight
        GetLayout()
        GetCoordinates()
        SplitBitmap(Source)
    End Sub

    Public Sub SaveBitmaps(TargetPath As String)
        For Each CubeFace In CubeBitmaps
            Dim FileName As String = Path.Combine(TargetPath, $"Face_{CubeFace.Key}.png")
            Dim encoder As New PngBitmapEncoder()
            encoder.Frames.Add(BitmapFrame.Create(CubeFace.Value))

            Using fs As New FileStream(FileName, FileMode.Create, FileAccess.Write)
                encoder.Save(fs)
            End Using
        Next
    End Sub

    Private Sub GetLayout()
        If Width = Height * 6 Then
            Layout = ImageLayout.StripHorizontal
            FaceSize = Height
        ElseIf Height = Width * 6 Then
            Layout = ImageLayout.StripVertical
            FaceSize = Width
        ElseIf Width * 3 = Height * 4 Then
            Layout = ImageLayout.CrossHorizontal
            FaceSize = Width \ 4
        ElseIf Width * 4 = Height * 3 Then
            Layout = ImageLayout.CrossVertical
            FaceSize = Height \ 4
        Else
            Throw New Exception("Unsupported aspect ratio.")
        End If
    End Sub

    Private Sub GetCoordinates()
        Select Case Layout
            Case ImageLayout.StripHorizontal
                FaceCoordinates.Add("PX", (0, 0)) : FaceCoordinates.Add("NX", (1, 0))
                FaceCoordinates.Add("PY", (2, 0)) : FaceCoordinates.Add("NY", (3, 0))
                FaceCoordinates.Add("PZ", (4, 0)) : FaceCoordinates.Add("NZ", (5, 0))
            Case ImageLayout.StripVertical
                FaceCoordinates.Add("PX", (0, 0)) : FaceCoordinates.Add("NX", (0, 1))
                FaceCoordinates.Add("PY", (0, 2)) : FaceCoordinates.Add("NY", (0, 3))
                FaceCoordinates.Add("PZ", (0, 4)) : FaceCoordinates.Add("NZ", (0, 5))
            Case ImageLayout.CrossHorizontal
                FaceCoordinates.Add("PY", (1, 0)) : FaceCoordinates.Add("NX", (0, 1))
                FaceCoordinates.Add("PZ", (1, 1)) : FaceCoordinates.Add("PX", (2, 1))
                FaceCoordinates.Add("NZ", (3, 1)) : FaceCoordinates.Add("NY", (1, 2))
            Case ImageLayout.CrossVertical
                FaceCoordinates.Add("PY", (1, 0)) : FaceCoordinates.Add("NX", (0, 1))
                FaceCoordinates.Add("PZ", (1, 1)) : FaceCoordinates.Add("PX", (2, 1))
                FaceCoordinates.Add("NY", (1, 2)) : FaceCoordinates.Add("NZ", (1, 3))
        End Select
    End Sub

    Private Sub SplitBitmap(Source As BitmapSource)
        For Each face In FaceCoordinates
            Dim x As Integer = face.Value.X * FaceSize
            Dim y As Integer = face.Value.Y * FaceSize
            Dim cropRect As New Windows.Int32Rect(x, y, FaceSize, FaceSize)
            Dim croppedFace As New CroppedBitmap(Source, cropRect)
            croppedFace.Freeze()
            CubeBitmaps.Add(face.Key, croppedFace)
        Next
    End Sub
End Class