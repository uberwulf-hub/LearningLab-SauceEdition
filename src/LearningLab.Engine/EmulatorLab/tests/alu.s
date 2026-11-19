# alu.s - arithmetic, comparisons, logic, shifts, lui / auipc (RV32I)
.include "_test.s"

.text
_start:
    # ADD / SUB wrap around at 32 bits
    TEST_RR  1, add, 0x00000000, 0x00000000, 0x00000000
    TEST_RR  2, add, 0x80000000, 0x7fffffff, 0x00000001
    TEST_RR  3, add, 0x00000000, 0xffffffff, 0x00000001
    TEST_RR  4, add, 0xfffffffe, 0xffffffff, 0xffffffff
    TEST_RR  5, sub, 0xffffffff, 0x00000000, 0x00000001
    TEST_RR  6, sub, 0x7fffffff, 0x80000000, 0x00000001
    TEST_RR  7, sub, 0x80000000, 0x00000000, 0x80000000
    # SLT signed, SLTU unsigned
    TEST_RR  8, slt,  1, 0xffffffff, 0x00000001
    TEST_RR  9, slt,  0, 0x00000001, 0xffffffff
    TEST_RR 10, sltu, 0, 0xffffffff, 0x00000001
    TEST_RR 11, sltu, 1, 0x00000001, 0xffffffff
    TEST_RR 12, slt,  0, 5, 5
    TEST_RR 13, slt,  1, 0x80000000, 0x7fffffff
    TEST_IMM 14, slti,  1, 0xffffffff, 0
    TEST_IMM 15, sltiu, 1, 0, -1                # the immediate is sign-extended, then compared unsigned
    TEST_IMM 16, sltiu, 0, 0xffffffff, -1
    TEST_IMM 17, sltiu, 1, 0x7fffffff, -2048
    # immediates are sign-extended 12-bit numbers
    TEST_IMM 18, addi, 0xfffff800, 0, -2048
    TEST_IMM 19, addi, 0x800007ff, 0x80000000, 2047
    TEST_IMM 20, xori, 0xffffff0f, 0x000000f0, -1
    TEST_IMM 21, ori,  0xfffff80f, 0x0000000f, -2048
    TEST_IMM 22, andi, 0x00000700, 0x00ff0f00, 0x7ff
    TEST_RR 23, xor, 0xf00ff00f, 0xff00ff00, 0x0f0f0f0f
    TEST_RR 24, or,  0xff0fff0f, 0xff00ff00, 0x0f0f0f0f
    TEST_RR 25, and, 0x0f000f00, 0xff00ff00, 0x0f0f0f0f
    # shifts use only the low 5 bits of the amount
    TEST_RR 26, sll, 0x00000001, 0x00000001, 0
    TEST_RR 27, sll, 0x80000000, 0x00000001, 31
    TEST_RR 28, sll, 0x80000000, 0x00000001, 63
    TEST_RR 29, sll, 0x00000001, 0x00000001, 32
    TEST_RR 30, srl, 0x00000001, 0x80000000, 31
    TEST_RR 31, srl, 0x80000000, 0x80000000, 0
    TEST_RR 32, sra, 0xffffffff, 0x80000000, 31
    TEST_RR 33, sra, 0xc0000000, 0x80000000, 1
    TEST_RR 34, sra, 0x3fffffff, 0x7fffffff, 1
    TEST_IMM 35, slli, 0x80000000, 1, 31
    TEST_IMM 36, srli, 0x00000001, 0x80000000, 31
    TEST_IMM 37, srai, 0xffffffff, 0x80000000, 31
    TEST_IMM 38, srai, 0x81818181, 0x81818181, 0
    TEST_IMM 39, srli, 0x01030307, 0x81818381, 7
    # LUI and AUIPC
    li   gp, 40
    lui  x14, 0xfffff
    li   x7, 0xfffff000
    bne  x14, x7, fail
    li   gp, 41
1:  auipc x14, 0
    la   x7, 1b
    bne  x14, x7, fail
    li   gp, 42
2:  auipc x14, 1
    la   x7, 2b
    sub  x14, x14, x7
    li   x7, 0x1000
    bne  x14, x7, fail
    # x0 is always zero
    li   gp, 43
    li   x1, 5
    add  x0, x1, x1
    bnez x0, fail
    li   gp, 44
    addi x0, x0, 7
    lui  x0, 0x12345
    bnez x0, fail
    TEST_DONE
