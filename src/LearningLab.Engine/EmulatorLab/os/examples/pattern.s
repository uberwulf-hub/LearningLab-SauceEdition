# pattern.s - colour every pixel of the screen with (x XOR y): a famous pattern that needs just one line of
# maths. Two loops, one inside the other: for each row y, for each column x, draw a pixel.
# Press a key to go back to LabOS.

.include "_syscalls.s"

.text
_start:
    li   a0, 1              # graphics mode
    li   a7, SYS_SCREEN_MODE
    ecall
    li   s1, 0              # s1 = y
row:
    li   s0, 0              # s0 = x
column:
    xor  t0, s0, s1         # the colour: x XOR y...
    andi t0, t0, 63         # ...kept between 0 and 63...
    addi a2, t0, 16         # ...from the palette's colour cube (16 onwards)
    mv   a0, s0
    mv   a1, s1
    li   a7, SYS_PIXEL
    ecall
    addi s0, s0, 1
    li   t1, 320
    blt  s0, t1, column     # next x
    addi s1, s1, 1
    li   t1, 200
    blt  s1, t1, row        # next y
    li   a7, SYS_GETKEY     # wait for a key
    ecall
    li   a0, 0
    li   a7, SYS_EXIT
    ecall
