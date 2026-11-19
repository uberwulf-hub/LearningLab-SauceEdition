# mem.s - loads and stores: sizes, sign extension, little-endian byte order, negative offsets
.include "_test.s"

.macro TEST_LD num, inst, expect, addr
    li   gp, \num
    \inst x14, \addr
    li   x7, \expect
    bne  x14, x7, fail
.endm

.text
_start:
    la   x5, tdat               # bytes 81 02 83 f4: the word 0xf4830281
    TEST_LD  1, lb,  0xffffff81, 0(x5)
    TEST_LD  2, lbu, 0x00000081, 0(x5)
    TEST_LD  3, lb,  0x00000002, 1(x5)
    TEST_LD  4, lb,  0xfffffff4, 3(x5)
    TEST_LD  5, lbu, 0x000000f4, 3(x5)
    TEST_LD  6, lh,  0x00000281, 0(x5)
    TEST_LD  7, lh,  0xfffff483, 2(x5)
    TEST_LD  8, lhu, 0x0000f483, 2(x5)
    TEST_LD  9, lw,  0xf4830281, 0(x5)
    addi x6, x5, 4
    TEST_LD 10, lw,  0xf4830281, -4(x6)
    TEST_LD 11, lbu, 0x00000083, -2(x6)
    TEST_LD 12, lw,  0x7ff00ff7, 4(x5)

    # stores write only their own bytes
    la   x5, buf
    li   x1, 0xaabbccdd
    sb   x1, 1(x5)
    TEST_LD 13, lw, 0x0000dd00, 0(x5)
    li   x1, 0x1234ffee
    sh   x1, 2(x5)
    TEST_LD 14, lw, 0xffeedd00, 0(x5)
    li   x1, 0x11223344
    sw   x1, 4(x5)
    TEST_LD 15, lbu, 0x44, 4(x5)         # little-endian: the lowest byte comes first
    TEST_LD 16, lbu, 0x33, 5(x5)
    TEST_LD 17, lbu, 0x22, 6(x5)
    TEST_LD 18, lbu, 0x11, 7(x5)
    addi x6, x5, 8
    li   x1, 0x55
    sb   x1, -1(x6)
    TEST_LD 19, lw, 0x55223344, 4(x5)
    li   x1, 0x7788
    sh   x1, -4(x6)
    TEST_LD 20, lw, 0x55227788, 4(x5)
    # a load into x0 is thrown away
    li   gp, 21
    lw   x0, 0(x5)
    bnez x0, fail
    # the "lw rd, label" pseudo-instruction
    li   gp, 22
    lw   x14, tdat
    li   x7, 0xf4830281
    bne  x14, x7, fail
    TEST_DONE

.data
.align 2
tdat: .byte 0x81, 0x02, 0x83, 0xf4
      .half 0x0ff7, 0x7ff0
buf:  .word 0, 0, 0
