# fibonacci.s - the Fibonacci numbers: each one is the sum of the two before it (0, 1, 1, 2, 3, 5, 8...).
# Shows a function: "call next" jumps to it and remembers where to come back; "ret" goes back.

.include "_syscalls.s"

.text
_start:
    la   a0, title
    li   a7, SYS_PRINT
    ecall
    li   s0, 0              # s0 and s1: the last two numbers
    li   s1, 1
    li   s2, 20             # how many to print
1:  mv   a0, s0
    call print_number
    call next
    addi s2, s2, -1
    bnez s2, 1b
    li   a0, '\n'
    li   a7, SYS_PUTCHAR
    ecall
    li   a0, 0
    li   a7, SYS_EXIT
    ecall

# next: move one step along: (s0, s1) becomes (s1, s0 + s1)
next:
    add  t0, s0, s1
    mv   s0, s1
    mv   s1, t0
    ret

# print_number(a0): the number and a space
print_number:
    li   a7, SYS_PRINT_NUM
    ecall
    li   a0, ' '
    li   a7, SYS_PUTCHAR
    ecall
    ret

.data
title: .asciz "The first 20 Fibonacci numbers:\n"
