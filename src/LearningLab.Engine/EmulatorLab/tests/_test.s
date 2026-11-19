# _test.s - shared parts of the processor tests. Each test program runs "bare metal" (no LabOS) from
# 0x80000000. gp holds the number of the check being made; at the end the program writes to the power register:
# 0x5555 = every check passed, 0x3333 + (check number << 16) = that check failed.

.equ POWER, 0x10004000

# TEST_RR: rd = a <op> b, then compare with the expected value
.macro TEST_RR num, inst, expect, a, b
    li   gp, \num
    li   x1, \a
    li   x2, \b
    \inst x14, x1, x2
    li   x7, \expect
    bne  x14, x7, fail
.endm

# TEST_IMM: rd = a <op> immediate
.macro TEST_IMM num, inst, expect, a, imm
    li   gp, \num
    li   x1, \a
    \inst x14, x1, \imm
    li   x7, \expect
    bne  x14, x7, fail
.endm

# TEST_EQ: a register must hold a value
.macro TEST_EQ num, reg, expect
    li   gp, \num
    li   x7, \expect
    bne  \reg, x7, fail
.endm

.macro PASS
    li   t6, POWER
    li   t5, 0x5555
    sw   t5, 0(t6)
1:  j    1b
.endm

# TEST_DONE: the end of a test program: report a pass, and the "fail" label every check jumps to
.macro TEST_DONE
    PASS
fail:
    slli t5, gp, 16
    li   t6, 0x3333
    or   t5, t5, t6
    li   t6, POWER
    sw   t5, 0(t6)
1:  j    1b
.endm
