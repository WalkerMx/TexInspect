Imports System

Public Module Program
    <STAThread>
    Public Sub Main()
        Dim app As New System.Windows.Application()
        Dim win As New TexInspect.MainWindow()
        app.Run(win)
    End Sub
End Module