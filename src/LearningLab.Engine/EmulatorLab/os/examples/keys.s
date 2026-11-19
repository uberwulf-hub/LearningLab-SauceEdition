# keys.s - show the code of every key you press: its ASCII code (letters, digits...) and its scancode (the
# number of the key on a PC keyboard; arrows have a scancode but no ASCII code). Esc stops.

.include "_syscalls.s"

.text
_start:
    la   a0, hello
    li   a7, SYS_PRINT
    ecall
loop:
    li   a7, SYS_GETKEY
    ecall
    beqz a0, done           # Ctrl+C
    mv   s0, a0
    la   a0, t_ascii
    li   a7, SYS_PRINT
    ecall
    andi a0, s0, 0xff       # bits 0-7: ASCII
    li   a7, SYS_PRINT_NUM
    ecall
    andi t0, s0, 0xff
    li   t1, 32
    blt  t0, t1, 1f         # a letter we can show?
    li   t1, 127
    bge  t0, t1, 1f
    la   a0, t_quote
    li   a7, SYS_PRINT
    ecall
    andi a0, s0, 0xff
    li   a7, SYS_PUTCHAR
    ecall
    li   a0, '\''
    li   a7, SYS_PUTCHAR
    ecall
1:  la   a0, t_scan
    li   a7, SYS_PRINT
    ecall
    srli a0, s0, 8          # bits 8-15: scancode
    andi a0, a0, 0xff
    li   a7, SYS_PRINT_NUM
    ecall
    li   a0, '\n'
    li   a7, SYS_PUTCHAR
    ecall
    srli t0, s0, 8
    li   t1, SCAN_ESC
    bne  t0, t1, loop
done:
    li   a0, 0
    li   a7, SYS_EXIT
    ecall

.data
hello:   .asciz "Press keys to see their codes. Esc stops.\n"
t_ascii: .asciz "ASCII "
t_quote: .asciz " '"
t_scan:  .asciz "   scancode "
