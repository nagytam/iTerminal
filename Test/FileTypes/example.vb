' A small Visual Basic (VB.NET) program: a greeting and the mean of some values.
Imports System

Module Program
    Private Const Count As Integer = 5

    Function Mean(values As Double()) As Double
        If values.Length = 0 Then Return 0
        Dim total As Double = 0
        For Each v As Double In values
            total += v
        Next
        Return total / values.Length
    End Function

    Sub Main()
        Dim values As Double() = {1.5, 2.0, 32.5, -0.4, 10}
        Console.WriteLine("Hello from iTerminal")
        If Mean(values) > 5 Then
            Console.WriteLine("Mean is large: " & Mean(values).ToString("F3"))
        Else
            Console.WriteLine("It said ""small""")
        End If
    End Sub
End Module
