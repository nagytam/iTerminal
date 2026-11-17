       IDENTIFICATION DIVISION.
       PROGRAM-ID. HELLO-TOTALS.
      *> A small program: sums the amounts and prints a greeting.
       ENVIRONMENT DIVISION.
       DATA DIVISION.
       WORKING-STORAGE SECTION.
       01  WS-GREETING      PIC X(30) VALUE "Hello from iTerminal".
       01  WS-COUNT         PIC 9(3)  VALUE ZERO.
       01  WS-TOTAL         PIC 9(7)V99 COMP-3 VALUE 0.
       01  WS-AMOUNT        PIC 9(5)V99.
       01  WS-EOF-FLAG      PIC X     VALUE 'N'.
           88  WS-EOF                 VALUE 'Y'.
       PROCEDURE DIVISION.
       MAIN-PARA.
           DISPLAY WS-GREETING
           PERFORM UNTIL WS-EOF
               ACCEPT WS-AMOUNT
               IF WS-AMOUNT = ZERO
                   SET WS-EOF TO TRUE
               ELSE
                   ADD WS-AMOUNT TO WS-TOTAL
                   ADD 1 TO WS-COUNT
               END-IF
           END-PERFORM
           COMPUTE WS-TOTAL ROUNDED = WS-TOTAL * 1.00
           DISPLAY "Items: " WS-COUNT " total: " WS-TOTAL
           STOP RUN.
