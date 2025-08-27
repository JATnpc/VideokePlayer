Imports System.IO.Ports
Imports System.Media
Imports System.IO

Public Class Form1
    ' Designer needs this:
    Public Sub New()
        InitializeComponent()
    End Sub

    Private ReadOnly player As New SoundPlayer()

    ' Code -> Title
    Private ReadOnly Titles As New Dictionary(Of String, String)()

    ' Code -> WAV path
    Private ReadOnly Paths As New Dictionary(Of String, String)()

    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Me.Text = "Videoke Player - Ready"
        Me.KeyPreview = True

        ' UI defaults
        txtCode.ReadOnly = True
        txtCode.Text = ""
        lblTitle.Text = ""
        lblStatus.Text = "Ready"

        Titles.Clear()
        Paths.Clear()
        lstSongs.Items.Clear()

        ' --- Load songs from CSV ---
        Dim filePath As String = "C:\Karaoke\songs.csv" ' make sure this exists
        If File.Exists(filePath) Then
            Dim added = 0
            For Each raw In File.ReadAllLines(filePath)
                Dim p = ParseCsvLine(raw)
                If Not p.ok Then Continue For
                Titles(p.code) = p.title
                Paths(p.code) = p.path
                lstSongs.Items.Add($"{p.code} - {p.title}")
                added += 1
            Next
            lblStatus.Text = $"✅ Songs loaded: {added}"
        Else
            lblStatus.Text = "⚠ songs.csv not found!"
        End If

        ' --- Serial setup (safe) ---
        SerialPort1.BaudRate = 9600
        SerialPort1.NewLine = vbLf
        SerialPort1.PortName = "COM3" ' change when UNO is connected
        Try
            If Not SerialPort1.IsOpen Then SerialPort1.Open()
            lblStatus.Text = "Listening on " & SerialPort1.PortName
        Catch ex As Exception
            ' It's fine if no UNO yet
            lblStatus.Text = "Serial not open: " & ex.Message
        End Try
    End Sub

    ' ===== Serial from Arduino =====
    Private Sub SerialPort1_DataReceived(sender As Object, e As SerialDataReceivedEventArgs) _
        Handles SerialPort1.DataReceived
        Try
            Dim line As String = SerialPort1.ReadLine().Trim()
            Me.BeginInvoke(Sub() HandleLine(line))
        Catch
            ' ignore malformed reads
        End Try
    End Sub

    Private Sub HandleLine(line As String)
        If line.StartsWith("CODE=", StringComparison.OrdinalIgnoreCase) Then
            Dim code = line.Substring(5)
            txtCode.Text = code
            SelectListItemForCode(code)
            lblStatus.Text = If(code = "", "Enter code…", "Typing…")

        ElseIf line.StartsWith("PLAY=", StringComparison.OrdinalIgnoreCase) Then
            Dim code = line.Substring(5)
            txtCode.Text = code
            SelectListItemForCode(code)
            PlayByCode(code)
        End If
    End Sub

    ' ===== Buttons =====
    Private Sub btnPlay_Click(sender As Object, e As EventArgs) Handles btnPlay.Click
        PlayByCode(txtCode.Text.Trim())
    End Sub

    Private Sub btnStop_Click(sender As Object, e As EventArgs) Handles btnStop.Click
        Try
            player.Stop()
        Finally
            lblTitle.Text = ""
            lblStatus.Text = "Stopped"
        End Try
    End Sub

    Private Sub btnClear_Click(sender As Object, e As EventArgs) Handles btnClear.Click
        txtCode.Text = ""
        lblTitle.Text = ""
        lblStatus.Text = "Cleared"
        lstSongs.ClearSelected()
    End Sub

    ' Double-click a song in the list to play it
    Private Sub lstSongs_DoubleClick(sender As Object, e As EventArgs) Handles lstSongs.DoubleClick
        If lstSongs.SelectedItem Is Nothing Then Return
        Dim line As String = lstSongs.SelectedItem.ToString()
        Dim code As String = line.Split("-"c)(0).Trim()
        txtCode.Text = code
        PlayByCode(code)
    End Sub

    ' Update code when selection changes (single-click)
    Private Sub lstSongs_SelectedIndexChanged(sender As Object, e As EventArgs) Handles lstSongs.SelectedIndexChanged
        If lstSongs.SelectedItem Is Nothing Then Return
        Dim line As String = lstSongs.SelectedItem.ToString()
        Dim code As String = line.Split("-"c)(0).Trim()
        txtCode.Text = code
        lblStatus.Text = "Selected " & code
    End Sub

    ' ===== Playback =====
    Private Sub PlayByCode(code As String)
        If String.IsNullOrWhiteSpace(code) Then
            lblStatus.Text = "Enter a code first"
            Return
        End If

        Dim title As String = Nothing
        Dim path As String = Nothing

        If Titles.TryGetValue(code, title) AndAlso Paths.TryGetValue(code, path) AndAlso IO.File.Exists(path) Then
            Try
                player.Stop()
                ' avoid stacking handlers
                RemoveHandler player.LoadCompleted, AddressOf AfterLoadPlay
                AddHandler player.LoadCompleted, AddressOf AfterLoadPlay

                player.SoundLocation = path
                lblTitle.Text = title
                lblStatus.Text = "Loading..."
                player.LoadAsync()
            Catch ex As Exception
                lblStatus.Text = "Play error: " & ex.Message
            End Try
        Else
            lblTitle.Text = ""
            lblStatus.Text = "❌ Song not found"
        End If
    End Sub

    Private Sub AfterLoadPlay(sender As Object, e As System.ComponentModel.AsyncCompletedEventArgs)
        RemoveHandler player.LoadCompleted, AddressOf AfterLoadPlay
        If e.Error Is Nothing Then
            player.Play()
            lblStatus.Text = "Playing"
        Else
            lblStatus.Text = "Load error: " & e.Error.Message
        End If
    End Sub

    ' Clean up
    Private Sub Form1_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        Try
            player.Stop()
            player.Dispose()
        Catch
        End Try
        Try
            If SerialPort1.IsOpen Then SerialPort1.Close()
        Catch
        End Try
    End Sub

    ' ===== Helpers =====
    ' Parse a CSV line as: code,title,path
    Private Function ParseCsvLine(line As String) As (ok As Boolean, code As String, title As String, path As String)
        If String.IsNullOrWhiteSpace(line) Then Return (False, "", "", "")
        Dim t = line.Trim()
        If t.StartsWith("#") Then Return (False, "", "", "") ' comment

        ' first comma ends code
        Dim i1 = t.IndexOf(","c)
        If i1 < 1 Then Return (False, "", "", "")
        Dim code = t.Substring(0, i1).Trim()

        ' last comma starts path (allows commas in title)
        Dim i2 = t.LastIndexOf(","c)
        If i2 <= i1 + 1 Then Return (False, "", "", "")
        Dim title = t.Substring(i1 + 1, i2 - i1 - 1).Trim()
        Dim path = t.Substring(i2 + 1).Trim()

        ' optional: strip surrounding quotes
        If title.StartsWith("""") AndAlso title.EndsWith("""") Then title = title.Substring(1, title.Length - 2)
        If path.StartsWith("""") AndAlso path.EndsWith("""") Then path = path.Substring(1, path.Length - 2)

        Return (True, code, title, path)
    End Function

    Private Sub SelectListItemForCode(code As String)
        If String.IsNullOrWhiteSpace(code) Then Return
        Dim target = code & " - "
        For i = 0 To lstSongs.Items.Count - 1
            Dim s = lstSongs.Items(i).ToString()
            If s.StartsWith(target, StringComparison.OrdinalIgnoreCase) Then
                lstSongs.SelectedIndex = i
                Exit For
            End If
        Next
    End Sub
End Class
