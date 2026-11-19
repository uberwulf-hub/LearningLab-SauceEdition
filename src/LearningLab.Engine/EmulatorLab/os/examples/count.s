# count.s - count from 1 to 10. A first loop: a register holds the count, add 1, compare, jump back.
# Press "Assemble & run". Then try changing 10 to 20, or counting in steps of 2.

.include "_syscalls.s"

.text
_start:
    li   s0, 1              # s0 = the number to print
    li   s1, 10             # s1 = where to stop
loop:
    mv   a0, s0
    li   a7, SYS_PRINT_NUM  # print the number...
    ecall
    li   a0, ' '
    li   a7, SYS_PUTCHAR    # ...and a space
    ecall
    addi s0, s0, 1          # next number
    ble  s0, s1, loop       # not past 10 yet? go round again
    li   a0, '\n'
    li   a7, SYS_PUTCHAR
    ecall
    li   a0, 0
    li   a7, SYS_EXIT
    ecall
