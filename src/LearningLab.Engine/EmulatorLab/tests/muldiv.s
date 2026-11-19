# muldiv.s - the M extension: multiply (low and high halves) and divide, including division by zero and the
# one overflow case (-2147483648 / -1). RISC-V never traps on these: the results are fixed by the manual.
.include "_test.s"

.text
_start:
    TEST_RR  1, mul,    0x00001200, 0x00007e00, 0xb6db6db7
    TEST_RR  2, mul,    0x00000001, 0xffffffff, 0xffffffff
    TEST_RR  3, mul,    0x80000000, 0x40000000, 0x00000002
    TEST_RR  4, mul,    0x00000000, 0x80000000, 0x00000002
    TEST_RR  5, mulh,   0xffffffff, 0xffffffff, 0x00000001
    TEST_RR  6, mulh,   0x00000000, 0xffffffff, 0xffffffff
    TEST_RR  7, mulh,   0xc0000000, 0x80000000, 0x7fffffff
    TEST_RR  8, mulh,   0x40000000, 0x80000000, 0x80000000
    TEST_RR  9, mulhu,  0xfffffffe, 0xffffffff, 0xffffffff
    TEST_RR 10, mulhu,  0x00000000, 0xffffffff, 0x00000001
    TEST_RR 11, mulhsu, 0xffffffff, 0xffffffff, 0xffffffff
    TEST_RR 12, mulhsu, 0x80000000, 0x80000000, 0xffffffff
    TEST_RR 13, mulhsu, 0x00000000, 0x00000001, 0xffffffff
    # division rounds towards zero
    TEST_RR 14, div,  3,          20, 6
    TEST_RR 15, div,  0xfffffffd, -20, 6
    TEST_RR 16, div,  0xfffffffd, 20, -6
    TEST_RR 17, div,  3,          -20, -6
    TEST_RR 18, div,  0xffffffff, 20, 0             # divide by zero: -1
    TEST_RR 19, div,  0x80000000, 0x80000000, -1    # overflow: the dividend
    TEST_RR 20, divu, 3,          20, 6
    TEST_RR 21, divu, 715827879,  -20, 6
    TEST_RR 22, divu, 0xffffffff, 20, 0             # divide by zero: all ones
    # the remainder has the sign of the dividend
    TEST_RR 23, rem,  2,          20, 6
    TEST_RR 24, rem,  0xfffffffe, -20, 6
    TEST_RR 25, rem,  2,          20, -6
    TEST_RR 26, rem,  0xfffffffe, -20, -6
    TEST_RR 27, rem,  20,         20, 0             # remainder by zero: the dividend
    TEST_RR 28, rem,  0,          0x80000000, -1    # overflow: 0
    TEST_RR 29, remu, 2,          20, 6
    TEST_RR 30, remu, 2,          -20, 6
    TEST_RR 31, remu, 20,         20, 0
    TEST_RR 32, div,  0xffffffff, 0, 0
    TEST_RR 33, remu, 0,          0, 0
    TEST_RR 34, divu, 1,          0x80000000, 0x80000000
    TEST_DONE
