; A small NASM program for Linux x86-64: prints a greeting and exits.
section .data
    msg     db "Hello from iTerminal", 10   ; the text and a newline
    len     equ $ - msg

section .bss
    buffer  resb 64

section .text
global _start
_start:
    mov     rax, 1          ; sys_write
    mov     rdi, 1          ; stdout
    lea     rsi, [rel msg]
    mov     rdx, len
    syscall
    xor     edi, edi        ; exit status 0
    mov     eax, 60         ; sys_exit
    syscall
